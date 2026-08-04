using System;
using System.Diagnostics;
using LightWall.Core.Models;
using LightWall.Core.Serialization;
using LightWall.Core.Simulation;

namespace LightWall.Core.Transport
{
    /// <summary>
    /// A transport that goes nowhere: it hands the bytes straight to a software
    /// model of the Arduino and reports what the wall would be showing.
    ///
    /// This is the "virtual wall". It lets the entire output pipeline - frame
    /// generation, rate limiting, packet building, transmission, receiving,
    /// validation, unpacking - be built and proven with no hardware attached.
    ///
    /// What that leaves for the real wall is only the genuinely physical
    /// unknowns: whether the wiring matches what we think, and how the relays
    /// behave. Everything else can be settled indoors.
    ///
    /// FAULT INJECTION
    ///
    /// This can be told to damage the stream on purpose - see
    /// ByteDropProbability and ByteCorruptionProbability below.
    ///
    /// That is not a gimmick. A real cable will occasionally lose or mangle a
    /// byte, and the interesting question is not whether that happens but
    /// whether everything downstream recovers gracefully when it does. Making a
    /// real cable misbehave on demand is very difficult. Here it is a slider.
    /// </summary>
    public sealed class LoopbackTransport : IWallTransport
    {
        /// <summary>
        /// Guards the receiver, which is written by the output thread and read
        /// by the interface thread.
        /// </summary>
        private readonly object _gate = new();

        /// <summary>The software model of the wall on the other end.</summary>
        private readonly VirtualWallReceiver _receiver = new();

        /// <summary>Supplies the timing the receiver's watchdog needs.</summary>
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>Decides which bytes get dropped or corrupted.</summary>
        private readonly Random _random;

        /// <summary>Count of bytes deliberately dropped.</summary>
        private int _bytesDropped;

        /// <summary>Count of bytes deliberately corrupted.</summary>
        private int _bytesCorrupted;

        /// <summary>
        /// Creates a loopback transport.
        /// </summary>
        /// <param name="faultSeed">
        /// Seeds the fault generator. Passing a fixed value makes a test
        /// reproducible; leaving it out gives different faults each run.
        /// </param>
        public LoopbackTransport(int? faultSeed = null)
        {
            _random = faultSeed.HasValue ? new Random(faultSeed.Value) : new Random();
        }

        /// <inheritdoc />
        public string Name => "Loopback (virtual wall)";

        /// <inheritdoc />
        public bool IsConnected { get; private set; }

        /// <summary>
        /// The chance of any given byte vanishing, from 0.0 to 1.0.
        ///
        /// A dropped byte is the nastiest ordinary fault, because it shifts
        /// every byte after it out of position. The receiver has to notice it is
        /// lost and hunt for the next real packet boundary.
        ///
        /// Even 0.001 is quite aggressive at thirty 9-byte packets a second -
        /// that is a fault roughly every four seconds.
        /// </summary>
        public double ByteDropProbability { get; set; }

        /// <summary>
        /// The chance of any given byte arriving with one bit flipped, from 0.0
        /// to 1.0.
        ///
        /// Gentler than a dropped byte, since the packet stays the right length.
        /// The checksum should catch it and the frame should be discarded.
        /// </summary>
        public double ByteCorruptionProbability { get; set; }

        /// <summary>Count of packets handed to this transport.</summary>
        public int PacketsSent { get; private set; }

        /// <summary>Count of bytes deliberately dropped by fault injection.</summary>
        public int BytesDropped
        {
            get { lock (_gate) { return _bytesDropped; } }
        }

        /// <summary>Count of bytes deliberately corrupted by fault injection.</summary>
        public int BytesCorrupted
        {
            get { lock (_gate) { return _bytesCorrupted; } }
        }

        /// <summary>Packets the virtual wall accepted as valid.</summary>
        public int ValidPacketsReceived
        {
            get { lock (_gate) { return _receiver.ValidPacketsReceived; } }
        }

        /// <summary>Packets the virtual wall rejected on checksum.</summary>
        public int ChecksumFailures
        {
            get { lock (_gate) { return _receiver.ChecksumFailures; } }
        }

        /// <summary>Bytes the virtual wall threw away while hunting for a packet.</summary>
        public int BytesDiscarded
        {
            get { lock (_gate) { return _receiver.BytesDiscarded; } }
        }

        /// <summary>True when the virtual wall has blanked itself from silence.</summary>
        public bool WatchdogTripped
        {
            get { lock (_gate) { return _receiver.WatchdogTripped; } }
        }

        /// <inheritdoc />
        public void Connect()
        {
            lock (_gate)
            {
                _receiver.Reset();
            }

            IsConnected = true;
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            IsConnected = false;
        }

        /// <inheritdoc />
        public void Send(byte[] packet)
        {
            if (packet is null)
            {
                throw new ArgumentNullException(nameof(packet));
            }

            if (!IsConnected)
            {
                return;
            }

            PacketsSent++;

            double nowSeconds = _clock.Elapsed.TotalSeconds;

            lock (_gate)
            {
                foreach (byte value in packet)
                {
                    if (ByteDropProbability > 0.0 && _random.NextDouble() < ByteDropProbability)
                    {
                        // The byte simply never arrives. Everything after it in
                        // this packet is now misaligned as far as the receiver
                        // is concerned.
                        _bytesDropped++;
                        continue;
                    }

                    byte delivered = value;

                    if (ByteCorruptionProbability > 0.0 && _random.NextDouble() < ByteCorruptionProbability)
                    {
                        // Flip one bit at random, as electrical noise would.
                        delivered ^= (byte)(1 << _random.Next(0, 8));
                        _bytesCorrupted++;
                    }

                    _receiver.ReceiveByte(delivered, nowSeconds);
                }

                // Give the watchdog a chance to notice silence. It matters here
                // because a long enough run of dropped bytes looks exactly like
                // a disconnected cable.
                _receiver.Update(nowSeconds);
            }
        }

        /// <summary>
        /// Lets the virtual wall's watchdog run even when nothing is being sent.
        ///
        /// Worth calling from the interface while displaying the virtual wall,
        /// so that stopping output visibly blanks it after the timeout - which
        /// is exactly what the real wall would do.
        /// </summary>
        public void UpdateWatchdog()
        {
            lock (_gate)
            {
                _receiver.Update(_clock.Elapsed.TotalSeconds);
            }
        }

        /// <summary>
        /// Copies what the virtual wall is displaying into a frame the caller
        /// owns.
        ///
        /// A copy rather than a reference, so the picture cannot change halfway
        /// through being drawn.
        /// </summary>
        public void CopyReceivedFrameTo(WallFrame destination)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            lock (_gate)
            {
                destination.CopyFrom(_receiver.CurrentFrame);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Disconnect();
        }
    }
}
