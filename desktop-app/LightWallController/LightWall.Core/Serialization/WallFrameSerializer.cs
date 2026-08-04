using System;
using System.Linq;
using LightWall.Core.Models;

namespace LightWall.Core.Serialization
{
    /// <summary>
    /// Converts wall states into the exact bytes sent down the serial cable to
    /// the Arduino, and back again.
    ///
    /// This class defines the shared language between two programs written in
    /// two languages running on two machines. If the C# side and the Arduino
    /// side disagree about a single bit, the wall shows nonsense. So the format
    /// is written out here in detail, and the Arduino firmware must match it
    /// exactly.
    ///
    /// ============================ PACKET FORMAT ============================
    ///
    /// Every packet is exactly 9 bytes. There are no variable-length packets,
    /// which keeps the Arduino's job simple: collect 9 bytes, check them, act.
    ///
    ///   Byte 0   Sync1     always 0xAA
    ///   Byte 1   Sync2     always 0x55
    ///   Byte 2   Command   what kind of packet this is (see PacketCommand)
    ///   Byte 3   Payload byte 0   \
    ///   Byte 4   Payload byte 1    |
    ///   Byte 5   Payload byte 2    |  the 35 bulbs, one bit each
    ///   Byte 6   Payload byte 3    |
    ///   Byte 7   Payload byte 4   /
    ///   Byte 8   Checksum  all of bytes 2 to 7 XOR-ed together
    ///
    /// ======================== WHY TWO SYNC BYTES ==========================
    ///
    /// The sync bytes mark where a packet begins. Serial is just a stream of
    /// bytes with no natural boundaries, so the receiver has to find the start
    /// of each packet itself.
    ///
    /// An earlier draft used one sync byte, 0xAA. The problem is that 0xAA is
    /// also a perfectly ordinary bulb pattern - it means "alternate bulbs lit",
    /// which happens constantly. Sparkle Storm produces a payload byte equal to
    /// 0xAA every couple of seconds.
    ///
    /// So if the receiver ever lost its place mid-stream, it could easily latch
    /// onto a payload byte, mistake it for the start of a packet, and stay
    /// misaligned. Requiring 0xAA followed immediately by 0x55 makes an
    /// accidental match far less likely, for the price of one extra byte.
    ///
    /// It reduces the risk rather than eliminating it, which is why the checksum
    /// still matters: a packet that passes both sync bytes but fails its
    /// checksum should be thrown away and resynchronisation started again.
    ///
    /// ========================= BIT ORDER - IMPORTANT =======================
    ///
    /// This is the detail most likely to cause a confusing bug, so it is spelled
    /// out precisely.
    ///
    /// The 35 bulbs are numbered 0 to 34 in row-major order, meaning we count
    /// along the top row first, then the second row, and so on:
    ///
    ///   bit 0  = row 0, column 0    (top-left)
    ///   bit 6  = row 0, column 6    (top-right)
    ///   bit 7  = row 1, column 0
    ///   bit 34 = row 4, column 6    (bottom-right)
    ///
    /// Those bits are packed into bytes LEAST SIGNIFICANT BIT FIRST:
    ///
    ///   payload byte 0, bit 0 (value 1)   = bulb 0
    ///   payload byte 0, bit 1 (value 2)   = bulb 1
    ///   payload byte 0, bit 7 (value 128) = bulb 7
    ///   payload byte 1, bit 0 (value 1)   = bulb 8
    ///
    /// Get this backwards on the Arduino and the wall will appear mirrored and
    /// scrambled in a way that looks like a wiring fault but is not.
    ///
    /// Bits 35 to 39 of the last payload byte are unused and always zero.
    ///
    /// A convenient coincidence worth knowing: this numbering matches the
    /// allLights[35] array in the original hand-written sketch exactly. Bulb
    /// number N here is allLights[N] there, so the firmware can index straight
    /// into that array with no translation.
    /// </summary>
    public static class WallFrameSerializer
    {
        /// <summary>
        /// First of the two bytes marking the start of a packet.
        /// </summary>
        public const byte SyncByte1 = 0xAA;

        /// <summary>
        /// Second of the two bytes marking the start of a packet.
        /// </summary>
        public const byte SyncByte2 = 0x55;

        /// <summary>
        /// How many bytes hold the bulb data. 35 bits needs 5 bytes, which
        /// holds 40 bits, leaving 5 spare.
        /// </summary>
        public const int PayloadLength = 5;

        /// <summary>
        /// Total size of every packet: 2 sync + 1 command + 5 payload + 1 checksum.
        /// </summary>
        public const int PacketLength = 9;

        /// <summary>
        /// Position of the command byte within a packet.
        /// </summary>
        public const int CommandIndex = 2;

        /// <summary>
        /// Position of the first payload byte within a packet.
        /// </summary>
        public const int PayloadStartIndex = 3;

        /// <summary>
        /// Position of the checksum byte within a packet.
        /// </summary>
        public const int ChecksumIndex = 8;

        /// <summary>
        /// Packs the 35 bulbs of a frame into 5 bytes.
        ///
        /// See the bit-order notes on this class for exactly which bit is which
        /// bulb.
        /// </summary>
        public static byte[] SerializeFrameData(WallFrame frame)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            var payload = new byte[PayloadLength];

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    if (!frame.GetCell(row, column))
                    {
                        continue;
                    }

                    int bitIndex = GetBitIndex(row, column);

                    // Which of the 5 bytes this bulb lives in, and which bit
                    // within that byte.
                    int byteIndex = bitIndex / 8;
                    int bitOffset = bitIndex % 8;

                    // "1 << bitOffset" builds a byte with a single bit set, and
                    // "|=" merges it in without disturbing bits already set.
                    payload[byteIndex] |= (byte)(1 << bitOffset);
                }
            }

            return payload;
        }

        /// <summary>
        /// Unpacks 5 payload bytes back into a wall frame.
        ///
        /// This is the exact reverse of SerializeFrameData, and it earns its
        /// place for two reasons.
        ///
        /// It lets a test pack a frame, unpack it again, and confirm it survived
        /// the round trip unchanged. That catches packing mistakes immediately
        /// instead of at the wall.
        ///
        /// It also serves as a worked reference for the Arduino code. The
        /// firmware has to do exactly this, in C++, and having a known-correct
        /// version to translate from is much safer than working it out twice.
        /// </summary>
        public static WallFrame DeserializeFrameData(byte[] payload)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.Length != PayloadLength)
            {
                throw new ArgumentException(
                    $"Payload must be exactly {PayloadLength} bytes, but was {payload.Length}.",
                    nameof(payload));
            }

            var frame = new WallFrame();

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    int bitIndex = GetBitIndex(row, column);
                    int byteIndex = bitIndex / 8;
                    int bitOffset = bitIndex % 8;

                    // "&" keeps only the one bit we care about. If the result is
                    // not zero, that bit was set, so the bulb is on.
                    bool isOn = (payload[byteIndex] & (1 << bitOffset)) != 0;

                    frame.SetCell(row, column, isOn);
                }
            }

            return frame;
        }

        /// <summary>
        /// Builds a complete 9-byte packet telling the wall to display a frame.
        /// This is the packet sent over and over during normal operation.
        /// </summary>
        public static byte[] CreateFramePacket(WallFrame frame)
        {
            return CreatePacket(PacketCommand.FrameUpdate, SerializeFrameData(frame));
        }

        /// <summary>
        /// Builds a packet telling the wall to switch every bulb off.
        ///
        /// Worth having as its own command rather than sending an all-zero
        /// frame, because it lets the firmware treat "go dark" as a deliberate
        /// instruction it can always obey - including as its response to losing
        /// contact with the app.
        /// </summary>
        public static byte[] CreateBlackoutPacket()
        {
            return CreatePacket(PacketCommand.Blackout, new byte[PayloadLength]);
        }

        /// <summary>
        /// Builds a keep-alive packet.
        ///
        /// The plan is for the firmware to blank the wall if it hears nothing
        /// for a while, so that a crashed or disconnected app leaves the wall
        /// dark rather than frozen mid-pattern. That safety net needs the app to
        /// keep saying "still here" during moments when no frames are being sent.
        /// </summary>
        public static byte[] CreateHeartbeatPacket()
        {
            return CreatePacket(PacketCommand.Heartbeat, new byte[PayloadLength]);
        }

        /// <summary>
        /// Assembles any packet from a command and a payload.
        /// </summary>
        public static byte[] CreatePacket(PacketCommand command, byte[] payload)
        {
            if (payload is null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.Length != PayloadLength)
            {
                throw new ArgumentException(
                    $"Payload must be exactly {PayloadLength} bytes, but was {payload.Length}.",
                    nameof(payload));
            }

            var packet = new byte[PacketLength];

            packet[0] = SyncByte1;
            packet[1] = SyncByte2;
            packet[CommandIndex] = (byte)command;

            Array.Copy(payload, 0, packet, PayloadStartIndex, PayloadLength);

            packet[ChecksumIndex] = CalculateChecksum((byte)command, payload);

            return packet;
        }

        /// <summary>
        /// Checks a received packet and, if it is valid, reports what it says.
        ///
        /// The app does not receive packets today. This exists so that tests can
        /// verify the packets we build are actually readable, and so the
        /// Arduino's validation logic has a reference version to match.
        /// </summary>
        /// <param name="packet">The 9 bytes to examine.</param>
        /// <param name="command">The command found, if the packet was valid.</param>
        /// <param name="frame">The wall state found, if the packet was valid.</param>
        /// <returns>True when the packet is well-formed and passes its checksum.</returns>
        public static bool TryParsePacket(
            byte[] packet,
            out PacketCommand command,
            out WallFrame? frame)
        {
            command = default;
            frame = null;

            if (packet is null || packet.Length != PacketLength)
            {
                return false;
            }

            if (packet[0] != SyncByte1 || packet[1] != SyncByte2)
            {
                return false;
            }

            var payload = new byte[PayloadLength];
            Array.Copy(packet, PayloadStartIndex, payload, 0, PayloadLength);

            byte expectedChecksum = CalculateChecksum(packet[CommandIndex], payload);

            if (packet[ChecksumIndex] != expectedChecksum)
            {
                return false;
            }

            command = (PacketCommand)packet[CommandIndex];
            frame = DeserializeFrameData(payload);
            return true;
        }

        /// <summary>
        /// Turns bytes into readable text such as "AA 55 01 7F 00 00 00 00 7E".
        ///
        /// Purely a debugging aid, shown in the packet preview box in the app so
        /// the outgoing data can be eyeballed.
        /// </summary>
        public static string ToHexString(byte[] bytes)
        {
            if (bytes is null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        /// <summary>
        /// Works out which of the 35 bit positions a given bulb occupies.
        ///
        /// Row-major order: the whole top row comes first, then the next row.
        /// Row 2, column 3 is bit (2 x 7) + 3 = 17.
        /// </summary>
        public static int GetBitIndex(int row, int column)
        {
            return (row * WallFrame.Columns) + column;
        }

        /// <summary>
        /// Combines the command and payload into a single check byte.
        ///
        /// XOR-ing bytes together produces a value that almost certainly changes
        /// if any byte is corrupted in transit. The receiver recalculates it and
        /// compares; a mismatch means the packet is damaged and should be
        /// discarded.
        ///
        /// This is an error check, not a security measure. It catches accidental
        /// corruption on a cable, which is the only thing that threatens us here.
        /// </summary>
        public static byte CalculateChecksum(byte command, byte[] payload)
        {
            byte checksum = command;

            foreach (byte b in payload)
            {
                checksum ^= b;
            }

            return checksum;
        }
    }
}
