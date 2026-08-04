using System;
using LightWall.Core.Models;
using LightWall.Core.Serialization;

namespace LightWall.Core.Simulation
{
    /// <summary>
    /// A software model of the Arduino firmware's receiving half.
    ///
    /// This class pretends to be the wall. Feed it the same bytes we would send
    /// down the serial cable, and it works out what the physical wall would
    /// display, using exactly the logic the firmware will use.
    ///
    /// WHY THIS IS WORTH BUILDING
    ///
    /// Three reasons, and the first is the one that matters most.
    ///
    /// 1. It tests the byte STREAM, not individual packets.
    ///
    ///    The existing serializer tests check that one packet is built
    ///    correctly. That is necessary but it misses an entire category of bug,
    ///    because serial is not a sequence of packets - it is an unbroken river
    ///    of bytes with no markers in it. Every framing bug lives in the gap
    ///    between "this packet is correct" and "the receiver can find where this
    ///    packet starts". Those bugs only appear when you feed a stream in.
    ///
    /// 2. It is the reference the firmware gets translated from.
    ///
    ///    The C++ version has to do precisely this. Having a known-correct
    ///    version that is already proven against tests is far safer than working
    ///    the logic out a second time from the specification and hoping the two
    ///    interpretations match.
    ///
    /// 3. It lets us break things on purpose.
    ///
    ///    We can drop a byte, corrupt a byte, or feed in pure noise, and confirm
    ///    the receiver recovers. Making a real cable drop a byte on demand is
    ///    very difficult; doing it here is one line of test code.
    ///
    /// WHAT IT DELIBERATELY DOES NOT MODEL
    ///
    /// Anything electrical. It cannot tell us whether the wall is wired the way
    /// we think it is, whether bulb 0 really is the top-left one, or how the
    /// relays behave when switched rapidly. Those need the real wall.
    /// </summary>
    public sealed class VirtualWallReceiver
    {
        /// <summary>
        /// How many bytes follow the two sync bytes: the command, five payload
        /// bytes, and the checksum.
        /// </summary>
        private const int BodyLength = WallFrameSerializer.PacketLength - 2;

        /// <summary>
        /// The stages of finding and reading a packet.
        ///
        /// This is called a "state machine": the receiver is always in exactly
        /// one of these states, and each incoming byte may move it to another.
        /// It is the standard way to read a protocol out of a stream, and it is
        /// how the firmware will be written too.
        /// </summary>
        private enum ReceiveState
        {
            /// <summary>Hunting for the first sync byte. Anything else is noise.</summary>
            WaitingForSync1,

            /// <summary>Saw 0xAA; the very next byte must be 0x55.</summary>
            WaitingForSync2,

            /// <summary>Both sync bytes seen; collecting the 7 bytes of body.</summary>
            CollectingBody
        }

        /// <summary>
        /// Holds the command, payload and checksum while they arrive one byte at
        /// a time.
        /// </summary>
        private readonly byte[] _body = new byte[BodyLength];

        /// <summary>What the modelled wall is currently displaying.</summary>
        private readonly WallFrame _frame = new();

        /// <summary>Where we are in the process of reading a packet.</summary>
        private ReceiveState _state = ReceiveState.WaitingForSync1;

        /// <summary>How many body bytes have arrived so far.</summary>
        private int _bodyBytesReceived;

        /// <summary>
        /// The time the last valid packet arrived, used by the watchdog.
        /// Negative means nothing has ever been received.
        /// </summary>
        private double _lastValidPacketTime = double.NegativeInfinity;

        /// <summary>
        /// What the modelled wall is displaying right now.
        ///
        /// Returns the receiver's own frame rather than a copy, so reading it is
        /// free. Anything needing a stable snapshot should copy it.
        /// </summary>
        public WallFrame CurrentFrame => _frame;

        /// <summary>
        /// How long the wall will keep showing a picture without hearing from
        /// the app before it blanks itself, in seconds.
        ///
        /// THE POINT OF THE WATCHDOG
        ///
        /// If the app crashes, the laptop sleeps, or somebody trips over the
        /// USB cable, the Arduino simply stops receiving. Without a watchdog it
        /// would hold the last frame it got, so the wall would be stuck with
        /// some arbitrary half-lit pattern burning away indefinitely.
        ///
        /// For something switching mains voltage, what happens when contact is
        /// lost should be a decision, not an accident. Going dark is the safe
        /// choice.
        /// </summary>
        public double WatchdogTimeoutSeconds { get; set; } = 1.0;

        /// <summary>
        /// True when the watchdog has fired and blanked the wall.
        /// </summary>
        public bool WatchdogTripped { get; private set; }

        /// <summary>Count of packets that arrived intact and were applied.</summary>
        public int ValidPacketsReceived { get; private set; }

        /// <summary>
        /// Count of packets that had both sync bytes but failed their checksum.
        ///
        /// A non-zero number here on real hardware means bytes are being
        /// corrupted in transit - worth investigating cable quality or baud rate.
        /// </summary>
        public int ChecksumFailures { get; private set; }

        /// <summary>
        /// Count of bytes thrown away while hunting for the start of a packet.
        ///
        /// A handful right after connecting is normal and expected, because the
        /// Arduino's bootloader chatters briefly when the port opens. A steadily
        /// climbing number during normal running means something is wrong.
        /// </summary>
        public int BytesDiscarded { get; private set; }

        /// <summary>Count of blackout commands acted on.</summary>
        public int BlackoutsReceived { get; private set; }

        /// <summary>Count of heartbeat packets acted on.</summary>
        public int HeartbeatsReceived { get; private set; }

        /// <summary>
        /// Feeds a block of bytes in, exactly as they would arrive over serial.
        /// </summary>
        /// <param name="bytes">The bytes received.</param>
        /// <param name="nowSeconds">The current time, for the watchdog.</param>
        public void ReceiveBytes(byte[] bytes, double nowSeconds)
        {
            if (bytes is null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            foreach (byte value in bytes)
            {
                ReceiveByte(value, nowSeconds);
            }

            Update(nowSeconds);
        }

        /// <summary>
        /// Feeds in one byte.
        ///
        /// THIS IS THE HEART OF THE WHOLE THING, and it is the method the
        /// firmware needs to mirror exactly.
        ///
        /// It handles one byte at a time on purpose. The firmware will be doing
        /// the same, because an Arduino reads its serial buffer a byte at a time
        /// and has nowhere near enough memory to hold a stream and study it.
        /// Writing it this way here keeps the two versions structurally
        /// identical and easy to compare.
        /// </summary>
        public void ReceiveByte(byte value, double nowSeconds)
        {
            switch (_state)
            {
                case ReceiveState.WaitingForSync1:
                    if (value == WallFrameSerializer.SyncByte1)
                    {
                        _state = ReceiveState.WaitingForSync2;
                    }
                    else
                    {
                        // Not the start of a packet, so discard it and keep
                        // hunting. This is what recovery looks like: after any
                        // disruption we simply throw bytes away until something
                        // that looks like a packet start turns up.
                        BytesDiscarded++;
                    }
                    break;

                case ReceiveState.WaitingForSync2:
                    if (value == WallFrameSerializer.SyncByte2)
                    {
                        _state = ReceiveState.CollectingBody;
                        _bodyBytesReceived = 0;
                    }
                    else if (value == WallFrameSerializer.SyncByte1)
                    {
                        // Another 0xAA. Stay here rather than starting over,
                        // because THIS 0xAA might be the real start of a packet
                        // and the previous one was noise.
                        //
                        // Getting this wrong is a genuine and easily missed bug:
                        // dropping back to hunting would make the receiver skip
                        // over a perfectly good "AA AA 55 ..." and lose a packet
                        // it could have read. There is a test for exactly this.
                        BytesDiscarded++;
                    }
                    else
                    {
                        // The 0xAA was a coincidence - a payload byte, most
                        // likely. Go back to hunting.
                        BytesDiscarded += 2;
                        _state = ReceiveState.WaitingForSync1;
                    }
                    break;

                case ReceiveState.CollectingBody:
                    _body[_bodyBytesReceived] = value;
                    _bodyBytesReceived++;

                    if (_bodyBytesReceived == BodyLength)
                    {
                        ProcessCompletedPacket(nowSeconds);
                        _state = ReceiveState.WaitingForSync1;
                    }
                    break;
            }
        }

        /// <summary>
        /// Checks whether the watchdog should fire.
        ///
        /// Worth calling regularly even when no bytes are arriving, since
        /// "no bytes are arriving" is precisely the condition it exists to catch.
        /// </summary>
        public void Update(double nowSeconds)
        {
            if (WatchdogTripped)
            {
                return;
            }

            // Nothing has ever been received, so there is no silence to measure
            // yet. Without this check the watchdog would fire instantly at
            // startup, before the app had a chance to say anything.
            if (double.IsNegativeInfinity(_lastValidPacketTime))
            {
                return;
            }

            if (nowSeconds - _lastValidPacketTime > WatchdogTimeoutSeconds)
            {
                WatchdogTripped = true;
                _frame.Clear();
            }
        }

        /// <summary>
        /// Wipes all state, as though the device had just been powered on.
        /// </summary>
        public void Reset()
        {
            _state = ReceiveState.WaitingForSync1;
            _bodyBytesReceived = 0;
            _lastValidPacketTime = double.NegativeInfinity;
            WatchdogTripped = false;

            ValidPacketsReceived = 0;
            ChecksumFailures = 0;
            BytesDiscarded = 0;
            BlackoutsReceived = 0;
            HeartbeatsReceived = 0;

            _frame.Clear();
        }

        /// <summary>
        /// A full body has arrived. Verify it, and act on it if it is sound.
        /// </summary>
        private void ProcessCompletedPacket(double nowSeconds)
        {
            // Body layout: [command][payload x 5][checksum]
            byte command = _body[0];

            var payload = new byte[WallFrameSerializer.PayloadLength];
            Array.Copy(_body, 1, payload, 0, WallFrameSerializer.PayloadLength);

            byte receivedChecksum = _body[BodyLength - 1];
            byte expectedChecksum = WallFrameSerializer.CalculateChecksum(command, payload);

            if (receivedChecksum != expectedChecksum)
            {
                // Either bytes were corrupted, or - more likely - we latched
                // onto a coincidental 0xAA 0x55 inside a payload and read
                // rubbish. Either way, throw it away and start hunting again.
                //
                // This is why the checksum matters even with two sync bytes:
                // the sync bytes make a false start unlikely, and the checksum
                // catches the ones that slip through.
                ChecksumFailures++;
                return;
            }

            // The packet is sound. Any valid packet counts as contact, so the
            // watchdog is satisfied even by a command that changes nothing.
            _lastValidPacketTime = nowSeconds;
            WatchdogTripped = false;
            ValidPacketsReceived++;

            switch ((PacketCommand)command)
            {
                case PacketCommand.FrameUpdate:
                    ApplyPayloadToFrame(payload);
                    break;

                case PacketCommand.Blackout:
                    _frame.Clear();
                    BlackoutsReceived++;
                    break;

                case PacketCommand.Heartbeat:
                    // Deliberately changes nothing on the wall. Its only job is
                    // to reset the watchdog above, so the wall stays lit during
                    // a stretch when no new frames are being sent.
                    HeartbeatsReceived++;
                    break;

                default:
                    // An unrecognised command from a newer app version. Ignore
                    // it rather than treating it as an error - that way old
                    // firmware keeps working when new commands are added, and
                    // only misses the features it does not understand.
                    break;
            }
        }

        /// <summary>
        /// Unpacks the 5 payload bytes into the wall frame.
        ///
        /// Written out here in full rather than calling the serializer's
        /// unpacking method, because this class is meant to be an independent
        /// model of the firmware. If it borrowed the app's own code, a mistake
        /// in that code would be invisible - the two would agree with each other
        /// while both being wrong. Doing it separately means the tests compare
        /// two independent implementations.
        ///
        /// This is also the exact loop the firmware needs:
        ///
        ///   for (int i = 0; i &lt; 35; i++) {
        ///     bool on = payload[i / 8] &amp; (1 &lt;&lt; (i % 8));
        ///     digitalWrite(allLights[i], on ? HIGH : LOW);
        ///   }
        ///
        /// Note that bulb number i maps straight onto allLights[i] from the
        /// original sketch, with no translation needed.
        /// </summary>
        private void ApplyPayloadToFrame(byte[] payload)
        {
            for (int bulbIndex = 0; bulbIndex < WallFrame.Rows * WallFrame.Columns; bulbIndex++)
            {
                int byteIndex = bulbIndex / 8;
                int bitOffset = bulbIndex % 8;

                bool isOn = (payload[byteIndex] & (1 << bitOffset)) != 0;

                // Row-major: the first 7 bulbs are the top row, and so on.
                int row = bulbIndex / WallFrame.Columns;
                int column = bulbIndex % WallFrame.Columns;

                _frame.SetCell(row, column, isOn);
            }
        }
    }
}
