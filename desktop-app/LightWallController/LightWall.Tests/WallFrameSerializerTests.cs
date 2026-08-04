using System;
using LightWall.Core.Models;
using LightWall.Core.Serialization;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the byte format sent to the Arduino.
    ///
    /// WHY THESE MATTER MORE THAN THE OTHERS
    ///
    /// This format is a contract between two programs written in two languages
    /// running on two different machines. When the wall eventually shows
    /// something wrong, the very first question will be "is the app sending the
    /// wrong bytes, or is the firmware reading them wrong?"
    ///
    /// These tests answer the first half of that question in about a second,
    /// which means every debugging session can start by ruling it out and
    /// focusing on the hardware instead.
    ///
    /// Several of them deliberately spell out exact expected byte values rather
    /// than computing them. That is the point: a test that recalculates the
    /// answer the same way the code does would happily agree with a bug. Writing
    /// the expected bytes by hand pins the format down so that any accidental
    /// change to it fails loudly.
    /// </summary>
    public class WallFrameSerializerTests
    {
        [Fact]
        public void EmptyFrame_PacksToAllZeroes()
        {
            var frame = new WallFrame();

            byte[] payload = WallFrameSerializer.SerializeFrameData(frame);

            Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 }, payload);
        }

        /// <summary>
        /// The single most important test in this file.
        ///
        /// It nails down that bit 0 means the top-left bulb, and that bits are
        /// packed least-significant-first. Getting this backwards in the
        /// firmware produces a wall that looks scrambled in a way that strongly
        /// resembles a wiring fault, and would cost hours to track down.
        /// </summary>
        [Theory]
        [InlineData(0, 0, 0, 0x01)]  // bulb 0  -> byte 0, bit 0
        [InlineData(0, 1, 0, 0x02)]  // bulb 1  -> byte 0, bit 1
        [InlineData(0, 6, 0, 0x40)]  // bulb 6  -> byte 0, bit 6 (end of top row)
        [InlineData(1, 0, 0, 0x80)]  // bulb 7  -> byte 0, bit 7 (row 1 starts mid-byte)
        [InlineData(1, 1, 1, 0x01)]  // bulb 8  -> byte 1, bit 0
        [InlineData(4, 6, 4, 0x04)]  // bulb 34 -> byte 4, bit 2 (bottom-right, the last one)
        public void SingleBulb_LandsInTheExpectedBit(
            int row,
            int column,
            int expectedByteIndex,
            byte expectedByteValue)
        {
            var frame = new WallFrame();
            frame.SetCell(row, column, true);

            byte[] payload = WallFrameSerializer.SerializeFrameData(frame);

            Assert.Equal(expectedByteValue, payload[expectedByteIndex]);

            // Every other byte must be untouched, proving one bulb sets exactly
            // one bit and nothing bleeds into its neighbours.
            for (int i = 0; i < WallFrameSerializer.PayloadLength; i++)
            {
                if (i != expectedByteIndex)
                {
                    Assert.Equal(0x00, payload[i]);
                }
            }
        }

        [Fact]
        public void TopRowLit_FillsTheLowestSevenBits()
        {
            var frame = new WallFrame();
            frame.SetRow(0, true);

            byte[] payload = WallFrameSerializer.SerializeFrameData(frame);

            // Seven bits set, starting from bit 0: 0111 1111 = 0x7F.
            Assert.Equal(0x7F, payload[0]);
        }

        [Fact]
        public void FullWall_SetsAllThirtyFiveBitsAndNoMore()
        {
            var frame = new WallFrame();
            frame.Fill();

            byte[] payload = WallFrameSerializer.SerializeFrameData(frame);

            // 35 bits fills the first four bytes completely, then 3 bits of the
            // fifth. The remaining 5 bits of that byte are unused and must stay
            // zero: 0000 0111 = 0x07.
            Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x07 }, payload);
        }

        [Fact]
        public void FramePacket_HasTheExpectedShape()
        {
            var frame = new WallFrame();

            byte[] packet = WallFrameSerializer.CreateFramePacket(frame);

            Assert.Equal(WallFrameSerializer.PacketLength, packet.Length);
            Assert.Equal(9, packet.Length);

            // An empty frame produces a packet we can write out in full:
            // sync, sync, command, five zero payload bytes, and a checksum that
            // is just the command byte since XOR-ing with zero changes nothing.
            Assert.Equal(
                new byte[] { 0xAA, 0x55, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 },
                packet);
        }

        [Fact]
        public void FullWallPacket_HasTheExpectedBytes()
        {
            var frame = new WallFrame();
            frame.Fill();

            byte[] packet = WallFrameSerializer.CreateFramePacket(frame);

            // Checksum worked through by hand:
            //   0x01 xor 0xFF = 0xFE
            //   0xFE xor 0xFF = 0x01
            //   0x01 xor 0xFF = 0xFE
            //   0xFE xor 0xFF = 0x01
            //   0x01 xor 0x07 = 0x06
            Assert.Equal(
                new byte[] { 0xAA, 0x55, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0x07, 0x06 },
                packet);
        }

        /// <summary>
        /// Packing a frame and unpacking it again must give back exactly what
        /// went in.
        ///
        /// This catches whole categories of mistake at once - a bit written to
        /// the wrong place, an off-by-one in the row maths, a dropped edge cell -
        /// without needing a separate test for each.
        /// </summary>
        [Fact]
        public void PackingThenUnpacking_ReturnsTheOriginalFrame()
        {
            var original = new WallFrame();

            // A deliberately awkward arrangement: all four corners, the centre,
            // and a few cells that straddle byte boundaries.
            original.SetCell(0, 0, true);
            original.SetCell(0, 6, true);
            original.SetCell(4, 0, true);
            original.SetCell(4, 6, true);
            original.SetCell(2, 3, true);
            original.SetCell(1, 0, true);
            original.SetCell(1, 1, true);
            original.SetCell(2, 2, true);

            byte[] payload = WallFrameSerializer.SerializeFrameData(original);
            WallFrame restored = WallFrameSerializer.DeserializeFrameData(payload);

            Assert.True(restored.ContentEquals(original));
        }

        [Fact]
        public void PackingThenUnpacking_SurvivesManyRandomFrames()
        {
            // A fixed seed keeps this repeatable. A test that fails only
            // sometimes is worse than no test, because nobody trusts it.
            var random = new Random(20260804);

            for (int attempt = 0; attempt < 200; attempt++)
            {
                var original = new WallFrame();
                original.Randomize(random);

                byte[] payload = WallFrameSerializer.SerializeFrameData(original);
                WallFrame restored = WallFrameSerializer.DeserializeFrameData(payload);

                Assert.True(restored.ContentEquals(original));
            }
        }

        [Fact]
        public void TryParsePacket_AcceptsAPacketWeBuilt()
        {
            var frame = new WallFrame();
            frame.SetCell(3, 4, true);

            byte[] packet = WallFrameSerializer.CreateFramePacket(frame);

            bool parsed = WallFrameSerializer.TryParsePacket(
                packet,
                out PacketCommand command,
                out WallFrame? restored);

            Assert.True(parsed);
            Assert.Equal(PacketCommand.FrameUpdate, command);
            Assert.NotNull(restored);
            Assert.True(restored!.ContentEquals(frame));
        }

        [Fact]
        public void TryParsePacket_RejectsACorruptedPayload()
        {
            var frame = new WallFrame();
            frame.Fill();

            byte[] packet = WallFrameSerializer.CreateFramePacket(frame);

            // Flip a single bit in the payload, imitating a byte damaged on the
            // cable. The checksum must notice.
            packet[WallFrameSerializer.PayloadStartIndex] ^= 0x01;

            bool parsed = WallFrameSerializer.TryParsePacket(packet, out _, out _);

            Assert.False(parsed);
        }

        [Theory]
        [InlineData(0)]  // wrong first sync byte
        [InlineData(1)]  // wrong second sync byte
        public void TryParsePacket_RejectsABadSyncByte(int syncByteIndex)
        {
            var frame = new WallFrame();
            byte[] packet = WallFrameSerializer.CreateFramePacket(frame);

            packet[syncByteIndex] = 0x00;

            bool parsed = WallFrameSerializer.TryParsePacket(packet, out _, out _);

            Assert.False(parsed);
        }

        [Fact]
        public void TryParsePacket_RejectsAPacketOfTheWrongLength()
        {
            byte[] tooShort = new byte[WallFrameSerializer.PacketLength - 1];

            bool parsed = WallFrameSerializer.TryParsePacket(tooShort, out _, out _);

            Assert.False(parsed);
        }

        [Fact]
        public void BlackoutAndHeartbeat_CarryTheRightCommands()
        {
            byte[] blackout = WallFrameSerializer.CreateBlackoutPacket();
            byte[] heartbeat = WallFrameSerializer.CreateHeartbeatPacket();

            Assert.Equal(
                (byte)PacketCommand.Blackout,
                blackout[WallFrameSerializer.CommandIndex]);

            Assert.Equal(
                (byte)PacketCommand.Heartbeat,
                heartbeat[WallFrameSerializer.CommandIndex]);

            // Both must still pass validation, since the firmware will check
            // every packet the same way regardless of its command.
            Assert.True(WallFrameSerializer.TryParsePacket(blackout, out _, out _));
            Assert.True(WallFrameSerializer.TryParsePacket(heartbeat, out _, out _));
        }

        /// <summary>
        /// Documents the reason there are two sync bytes rather than one.
        ///
        /// An ordinary bulb pattern can produce a payload byte identical to the
        /// first sync byte. This test builds such a pattern deliberately, to
        /// record that this is expected and not a fault.
        ///
        /// It is why the firmware must never treat a lone 0xAA as proof that a
        /// packet is starting.
        /// </summary>
        [Fact]
        public void AnOrdinaryPattern_CanProduceAPayloadByteEqualToTheSyncByte()
        {
            var frame = new WallFrame();

            // Bits 1, 3, 5 and 7 set gives 1010 1010 = 0xAA.
            frame.SetCell(0, 1, true);  // bit 1
            frame.SetCell(0, 3, true);  // bit 3
            frame.SetCell(0, 5, true);  // bit 5
            frame.SetCell(1, 0, true);  // bit 7

            byte[] payload = WallFrameSerializer.SerializeFrameData(frame);

            Assert.Equal(WallFrameSerializer.SyncByte1, payload[0]);
        }

        [Fact]
        public void GetBitIndex_NumbersBulbsAcrossRowsFirst()
        {
            Assert.Equal(0, WallFrameSerializer.GetBitIndex(0, 0));
            Assert.Equal(6, WallFrameSerializer.GetBitIndex(0, 6));
            Assert.Equal(7, WallFrameSerializer.GetBitIndex(1, 0));
            Assert.Equal(17, WallFrameSerializer.GetBitIndex(2, 3));
            Assert.Equal(34, WallFrameSerializer.GetBitIndex(4, 6));
        }

        [Fact]
        public void DeserializeFrameData_RejectsAPayloadOfTheWrongSize()
        {
            Assert.Throws<ArgumentException>(
                () => WallFrameSerializer.DeserializeFrameData(new byte[4]));
        }

        [Fact]
        public void ToHexString_FormatsBytesReadably()
        {
            byte[] bytes = { 0xAA, 0x55, 0x01, 0x00 };

            Assert.Equal("AA 55 01 00", WallFrameSerializer.ToHexString(bytes));
        }
    }
}
