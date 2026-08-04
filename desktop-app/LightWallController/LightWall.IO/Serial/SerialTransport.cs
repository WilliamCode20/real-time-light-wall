using System;
using System.Diagnostics;
using System.IO.Ports;
using LightWall.Core.Transport;

namespace LightWall.IO.Serial
{
    /// <summary>
    /// Sends wall packets down a real USB serial cable to the Arduino.
    ///
    /// This is the counterpart to LoopbackTransport. Both implement
    /// IWallTransport, so everything upstream - the show clock, the rate
    /// limiting, the packet building, the error handling - is identical
    /// whichever one is attached.
    ///
    /// That is the payoff from building the loopback first: this class is the
    /// only genuinely new code needed to drive real hardware, and every hour
    /// spent testing against the virtual wall was testing the same code path
    /// that runs here.
    ///
    /// ================ THE RESET ON OPENING - READ THIS FIRST ================
    ///
    /// Opening a serial port to an Arduino Mega REBOOTS THE BOARD.
    ///
    /// The board watches the DTR line, which is a signal the computer raises
    /// when it opens a connection. On the Arduino that line is wired through a
    /// capacitor to the reset pin, deliberately - it is how the Arduino IDE
    /// makes the board restart before uploading a sketch, with no button press.
    ///
    /// The consequence is that for roughly one and a half to two seconds after
    /// connecting, the bootloader is running rather than our firmware. Anything
    /// sent during that window is ignored or swallowed.
    ///
    /// This catches everybody exactly once, and it is baffling when it does: the
    /// port opens without error, packets are apparently sent successfully, and
    /// the wall does nothing at all. It looks like a wiring fault or a protocol
    /// mistake, and it is neither.
    ///
    /// This class handles it by waiting out that window before sending anything.
    /// Rather than blocking for two seconds inside Connect - which would freeze
    /// the interface, since Connect is called from the window - it records when
    /// the port opened and quietly discards packets until the board has had time
    /// to finish starting up. Output begins on its own a moment later.
    ///
    /// The alternative would be to try to prevent the reset. That is not
    /// reliable across different drivers and USB chips, and it would also mean
    /// the board keeps whatever state it was left in. Letting it reset cleanly
    /// and waiting is both simpler and more predictable.
    /// </summary>
    public sealed class SerialTransport : IWallTransport
    {
        /// <summary>
        /// How long to wait after opening the port before sending anything, in
        /// seconds.
        ///
        /// The Mega's bootloader waits about a second for an upload before
        /// handing over to the sketch. Two seconds gives comfortable margin, and
        /// costs nothing but a brief pause when connecting.
        ///
        /// Worth confirming against the real board: if the wall stays dark for
        /// noticeably longer than this after connecting, raise it.
        /// </summary>
        public const double DefaultResetSettleSeconds = 2.0;

        /// <summary>
        /// The speed to talk at, in bits per second.
        ///
        /// 115200 is the usual choice for a Mega and is comfortably within what
        /// its USB bridge chip handles reliably.
        ///
        /// There is enormous headroom here. Nine bytes thirty times a second is
        /// 270 bytes per second, roughly 2% of this rate. Speed is not a
        /// constraint on this project; the relays are.
        /// </summary>
        public const int DefaultBaudRate = 115200;

        /// <summary>
        /// How long to allow a single write before giving up, in milliseconds.
        ///
        /// Without a limit, a write to a port whose far end has stopped
        /// listening can block indefinitely, and the output thread would hang
        /// there forever with no way back short of restarting the app.
        ///
        /// A quarter of a second is many times longer than a healthy 9-byte
        /// write needs, so hitting this means something is genuinely wrong.
        /// </summary>
        private const int WriteTimeoutMilliseconds = 250;

        /// <summary>
        /// Guards the port. Send comes from the output thread while Connect and
        /// Disconnect come from the interface thread.
        /// </summary>
        private readonly object _gate = new();

        /// <summary>Measures how long since the port was opened.</summary>
        private readonly Stopwatch _sinceOpened = new();

        /// <summary>The open port, or null when disconnected.</summary>
        private SerialPort? _port;

        /// <summary>
        /// Creates a transport for a named port.
        /// </summary>
        /// <param name="portName">A port name such as "COM3".</param>
        /// <param name="baudRate">Speed in bits per second.</param>
        /// <param name="resetSettleSeconds">
        /// How long to stay quiet after opening, while the board restarts.
        /// </param>
        public SerialTransport(
            string portName,
            int baudRate = DefaultBaudRate,
            double resetSettleSeconds = DefaultResetSettleSeconds)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                throw new ArgumentException("A port name is required.", nameof(portName));
            }

            PortName = portName;
            BaudRate = baudRate;
            ResetSettleSeconds = resetSettleSeconds;
        }

        /// <summary>The port being used, such as "COM3".</summary>
        public string PortName { get; }

        /// <summary>The speed in bits per second.</summary>
        public int BaudRate { get; }

        /// <summary>How long to stay quiet after opening, while the board restarts.</summary>
        public double ResetSettleSeconds { get; }

        /// <inheritdoc />
        public string Name => $"{PortName} at {BaudRate} baud";

        /// <inheritdoc />
        public bool IsConnected
        {
            get
            {
                lock (_gate)
                {
                    return _port is { IsOpen: true };
                }
            }
        }

        /// <summary>
        /// True while waiting out the board's reset, just after connecting.
        ///
        /// Worth showing in the interface. Without it, the first couple of
        /// seconds look identical to a broken connection.
        /// </summary>
        public bool IsWaitingForBoardReset
        {
            get
            {
                lock (_gate)
                {
                    return _port is { IsOpen: true }
                        && _sinceOpened.Elapsed.TotalSeconds < ResetSettleSeconds;
                }
            }
        }

        /// <summary>
        /// Packets discarded while waiting for the board to finish restarting.
        ///
        /// Expect roughly sixty of these on every connection at thirty packets a
        /// second. That is normal and not a fault.
        /// </summary>
        public int PacketsDroppedDuringReset { get; private set; }

        /// <summary>Packets successfully written to the port.</summary>
        public int PacketsWritten { get; private set; }

        /// <summary>
        /// The most recent failure, or null if nothing has gone wrong.
        ///
        /// Kept rather than thrown onward, because a failure on the output
        /// thread has nowhere useful to go - but the interface still needs to be
        /// able to say what happened.
        /// </summary>
        public string? LastError { get; private set; }

        /// <summary>
        /// Opens the port.
        ///
        /// Returns promptly. It does NOT wait out the board's reset - that would
        /// freeze the interface for two seconds. Packets sent during the settle
        /// window are quietly discarded instead, and output starts by itself
        /// once the board is ready.
        /// </summary>
        public void Connect()
        {
            lock (_gate)
            {
                if (_port is { IsOpen: true })
                {
                    return;
                }

                LastError = null;
                PacketsDroppedDuringReset = 0;
                PacketsWritten = 0;

                var port = new SerialPort(PortName, BaudRate, Parity.None, 8, StopBits.One)
                {
                    WriteTimeout = WriteTimeoutMilliseconds,

                    // Raising DTR is what resets the board. Doing it explicitly
                    // makes the reset deliberate and predictable rather than
                    // dependent on how a particular driver behaves - we then
                    // know exactly what we are waiting for.
                    DtrEnable = true,

                    // No hardware flow control. The Arduino does not use it, and
                    // leaving it on would stall writes waiting for a signal that
                    // never comes.
                    Handshake = Handshake.None
                };

                try
                {
                    port.Open();
                }
                catch (Exception ex)
                {
                    // The usual causes: the port does not exist, or something
                    // else already has it open - very often the Arduino IDE's
                    // serial monitor, which is worth checking first.
                    LastError = $"Could not open {PortName}: {ex.Message}";
                    port.Dispose();
                    throw;
                }

                _port = port;
                _sinceOpened.Restart();
            }
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            lock (_gate)
            {
                if (_port is null)
                {
                    return;
                }

                try
                {
                    if (_port.IsOpen)
                    {
                        _port.Close();
                    }
                }
                catch (Exception ex)
                {
                    // Closing a port whose cable has already been pulled can
                    // throw. There is nothing useful to do about it, and letting
                    // it escape would stop the object below from being disposed.
                    LastError = $"Error while closing {PortName}: {ex.Message}";
                }

                _port.Dispose();
                _port = null;
                _sinceOpened.Stop();
            }
        }

        /// <summary>
        /// Writes one packet to the port.
        ///
        /// Called from the output service's background thread, so blocking here
        /// is acceptable and cannot affect the interface.
        /// </summary>
        public void Send(byte[] packet)
        {
            if (packet is null)
            {
                throw new ArgumentNullException(nameof(packet));
            }

            lock (_gate)
            {
                if (_port is not { IsOpen: true })
                {
                    return;
                }

                // Still restarting - the bootloader would swallow this anyway,
                // so there is no point sending it.
                if (_sinceOpened.Elapsed.TotalSeconds < ResetSettleSeconds)
                {
                    PacketsDroppedDuringReset++;
                    return;
                }

                try
                {
                    _port.Write(packet, 0, packet.Length);
                    PacketsWritten++;
                }
                catch (Exception ex)
                {
                    // Almost always an unplugged cable. Record it and let the
                    // exception reach the output service, which drops the packet
                    // and tries again in a thirtieth of a second - so output
                    // resumes by itself if the cable comes back.
                    LastError = $"Write to {PortName} failed: {ex.Message}";
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Disconnect();
        }
    }
}
