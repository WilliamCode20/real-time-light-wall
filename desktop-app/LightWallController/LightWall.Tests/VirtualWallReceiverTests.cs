using System;
using System.Collections.Generic;
using System.Linq;
using LightWall.Core.Models;
using LightWall.Core.Serialization;
using LightWall.Core.Simulation;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the software model of the Arduino's receiving logic.
    ///
    /// These are the tests that would be painful or impossible to run against
    /// real hardware. Making an actual USB cable drop exactly one byte on
    /// command is very hard; doing it here is a single line.
    ///
    /// They matter because framing bugs do not show up when checking one packet
    /// in isolation. Serial is an unbroken stream of bytes with no markers in
    /// it, and the interesting failures all live in "the receiver lost its
    /// place - can it find its way back?".
    /// </summary>
    public class VirtualWallReceiverTests
    {
        /// <summary>
        /// Builds a frame with a recognisable arrangement, so a test can tell at
        /// a glance whether the right one arrived.
        /// </summary>
        private static WallFrame MakeFrame(params (int Row, int Column)[] litCells)
        {
            var frame = new WallFrame();

            foreach ((int row, int column) in litCells)
            {
                frame.SetCell(row, column, true);
            }

            return frame;
        }

        [Fact]
        public void CleanPacket_IsReceivedAndDisplayed()
        {
            var receiver = new VirtualWallReceiver();
            WallFrame sent = MakeFrame((0, 0), (2, 3), (4, 6));

            receiver.ReceiveBytes(WallFrameSerializer.CreateFramePacket(sent), nowSeconds: 0.0);

            Assert.Equal(1, receiver.ValidPacketsReceived);
            Assert.Equal(0, receiver.ChecksumFailures);
            Assert.Equal(0, receiver.BytesDiscarded);
            Assert.True(receiver.CurrentFrame.ContentEquals(sent));
        }

        [Fact]
        public void PacketSplitAcrossSeveralReads_StillArrives()
        {
            // Real serial delivers bytes whenever it feels like it. A packet may
            // arrive as one block, or in three pieces, or a byte at a time. The
            // receiver must not care.
            var receiver = new VirtualWallReceiver();
            WallFrame sent = MakeFrame((1, 1), (3, 5));

            byte[] packet = WallFrameSerializer.CreateFramePacket(sent);

            foreach (byte value in packet)
            {
                receiver.ReceiveBytes(new[] { value }, nowSeconds: 0.0);
            }

            Assert.Equal(1, receiver.ValidPacketsReceived);
            Assert.True(receiver.CurrentFrame.ContentEquals(sent));
        }

        [Fact]
        public void ContinuousStream_ReceivesEveryPacket()
        {
            var receiver = new VirtualWallReceiver();
            var random = new Random(4242);

            var stream = new List<byte>();
            var sentFrames = new List<WallFrame>();

            for (int i = 0; i < 100; i++)
            {
                var frame = new WallFrame();
                frame.Randomize(random);
                sentFrames.Add(frame);
                stream.AddRange(WallFrameSerializer.CreateFramePacket(frame));
            }

            receiver.ReceiveBytes(stream.ToArray(), nowSeconds: 0.0);

            Assert.Equal(100, receiver.ValidPacketsReceived);
            Assert.Equal(0, receiver.ChecksumFailures);
            Assert.Equal(0, receiver.BytesDiscarded);

            // The wall should be showing the last frame sent.
            Assert.True(receiver.CurrentFrame.ContentEquals(sentFrames[^1]));
        }

        /// <summary>
        /// The receiver and the serializer must agree, but they were written
        /// separately on purpose.
        ///
        /// The receiver unpacks bits with its own loop rather than calling the
        /// serializer's unpacking method. If it borrowed that code, a bug in the
        /// bit maths would be invisible - both sides would agree with each other
        /// while both being wrong, and the mistake would only surface as a
        /// scrambled physical wall.
        /// </summary>
        [Fact]
        public void ReceiverAgreesWithSerializerAcrossManyRandomFrames()
        {
            var receiver = new VirtualWallReceiver();
            var random = new Random(31337);

            for (int i = 0; i < 300; i++)
            {
                var sent = new WallFrame();
                sent.Randomize(random);

                receiver.ReceiveBytes(WallFrameSerializer.CreateFramePacket(sent), nowSeconds: i * 0.01);

                Assert.True(
                    receiver.CurrentFrame.ContentEquals(sent),
                    $"Frame {i} did not survive the round trip.");
            }
        }

        [Fact]
        public void GarbageBeforeAPacket_IsDiscardedAndThePacketStillArrives()
        {
            // This is what connecting to a running Arduino looks like: we start
            // listening mid-stream and have no idea where we are.
            var receiver = new VirtualWallReceiver();
            WallFrame sent = MakeFrame((2, 2));

            var stream = new List<byte> { 0x12, 0x34, 0x00, 0xFF, 0x99 };
            stream.AddRange(WallFrameSerializer.CreateFramePacket(sent));

            receiver.ReceiveBytes(stream.ToArray(), nowSeconds: 0.0);

            Assert.Equal(1, receiver.ValidPacketsReceived);
            Assert.Equal(5, receiver.BytesDiscarded);
            Assert.True(receiver.CurrentFrame.ContentEquals(sent));
        }

        [Fact]
        public void DoubledSyncByte_DoesNotSwallowTheFollowingPacket()
        {
            // A stray 0xAA immediately before a real packet gives the stream
            // "AA AA 55 ...".
            //
            // If the receiver dropped back to hunting on seeing the second 0xAA,
            // it would consume the real packet's first sync byte and lose the
            // whole packet. Staying put and treating each 0xAA as a possible
            // fresh start is what makes this work.
            //
            // This is a small, easy detail to get wrong, and the resulting bug -
            // occasional dropped frames - would be maddening to diagnose on
            // hardware.
            var receiver = new VirtualWallReceiver();
            WallFrame sent = MakeFrame((0, 6), (4, 0));

            var stream = new List<byte> { WallFrameSerializer.SyncByte1 };
            stream.AddRange(WallFrameSerializer.CreateFramePacket(sent));

            receiver.ReceiveBytes(stream.ToArray(), nowSeconds: 0.0);

            Assert.Equal(1, receiver.ValidPacketsReceived);
            Assert.True(receiver.CurrentFrame.ContentEquals(sent));
        }

        [Fact]
        public void CorruptedByte_IsCaughtByTheChecksum()
        {
            var receiver = new VirtualWallReceiver();
            WallFrame sent = MakeFrame((1, 2), (3, 4));

            byte[] packet = WallFrameSerializer.CreateFramePacket(sent);

            // Flip one bit in the payload, as a noisy cable would.
            packet[WallFrameSerializer.PayloadStartIndex] ^= 0x08;

            receiver.ReceiveBytes(packet, nowSeconds: 0.0);

            Assert.Equal(0, receiver.ValidPacketsReceived);
            Assert.Equal(1, receiver.ChecksumFailures);

            // Nothing was applied, so the wall stays dark rather than showing a
            // corrupted picture. Discarding a bad frame is always right here:
            // another one is along in a thirtieth of a second.
            Assert.Equal(0, receiver.CurrentFrame.CountLitCells());
        }

        [Fact]
        public void AfterACorruptedPacket_TheNextGoodOneStillArrives()
        {
            var receiver = new VirtualWallReceiver();

            byte[] bad = WallFrameSerializer.CreateFramePacket(MakeFrame((0, 0)));
            bad[WallFrameSerializer.PayloadStartIndex] ^= 0x01;

            WallFrame good = MakeFrame((4, 6), (4, 5));

            var stream = new List<byte>();
            stream.AddRange(bad);
            stream.AddRange(WallFrameSerializer.CreateFramePacket(good));

            receiver.ReceiveBytes(stream.ToArray(), nowSeconds: 0.0);

            Assert.Equal(1, receiver.ChecksumFailures);
            Assert.Equal(1, receiver.ValidPacketsReceived);
            Assert.True(receiver.CurrentFrame.ContentEquals(good));
        }

        [Fact]
        public void ADroppedByte_CostsOnePacketButTheStreamRecovers()
        {
            // A dropped byte is the nastiest ordinary failure, because it
            // shifts every following byte out of position. The receiver has to
            // notice it is lost and find the next real packet boundary.
            var receiver = new VirtualWallReceiver();
            var random = new Random(777);

            var frames = new List<WallFrame>();
            var stream = new List<byte>();

            for (int i = 0; i < 6; i++)
            {
                var frame = new WallFrame();
                frame.Randomize(random);
                frames.Add(frame);

                byte[] packet = WallFrameSerializer.CreateFramePacket(frame);

                if (i == 2)
                {
                    // Lose one byte from the middle of the third packet.
                    stream.AddRange(packet.Take(4));
                    stream.AddRange(packet.Skip(5));
                }
                else
                {
                    stream.AddRange(packet);
                }
            }

            receiver.ReceiveBytes(stream.ToArray(), nowSeconds: 0.0);

            // Whatever happened in the middle, the important outcome is that the
            // receiver got back in step and finished showing the correct frame.
            Assert.True(
                receiver.CurrentFrame.ContentEquals(frames[^1]),
                "Receiver failed to resynchronise after a dropped byte.");

            // It should have lost at most a couple of packets, not the rest of
            // the stream.
            Assert.True(
                receiver.ValidPacketsReceived >= 4,
                $"Expected to recover most packets, got {receiver.ValidPacketsReceived} of 6.");
        }

        /// <summary>
        /// The scenario the two sync bytes exist to guard against, tested
        /// honestly rather than optimistically.
        ///
        /// A payload can legitimately contain 0xAA followed by 0x55. While the
        /// receiver is reading a packet body that is harmless - it is counting
        /// bytes, not looking for sync markers. The danger is when it is lost
        /// and hunting: it can latch onto that pair inside a payload and start
        /// reading a packet from the wrong place.
        ///
        /// It cannot be prevented outright. What matters is that the checksum
        /// catches it and the receiver gets back in step shortly afterwards,
        /// which is what this checks.
        /// </summary>
        [Fact]
        public void APayloadContainingBothSyncBytes_DoesNotPermanentlyDerailTheStream()
        {
            // Build a frame whose first two payload bytes are exactly 0xAA 0x55.
            //
            // payload[0] = 0xAA needs bits 1, 3, 5 and 7  -> bulbs 1, 3, 5, 7
            // payload[1] = 0x55 needs bits 8, 10, 12, 14  -> bulbs 8, 10, 12, 14
            WallFrame trap = MakeFrame(
                (0, 1), (0, 3), (0, 5), (1, 0),
                (1, 1), (1, 3), (1, 5), (2, 0));

            byte[] trapPayload = WallFrameSerializer.SerializeFrameData(trap);

            // Confirm the trap is actually a trap before relying on it.
            Assert.Equal(WallFrameSerializer.SyncByte1, trapPayload[0]);
            Assert.Equal(WallFrameSerializer.SyncByte2, trapPayload[1]);

            var receiver = new VirtualWallReceiver();

            // In a clean stream this must simply be read correctly - the
            // receiver is counting body bytes, not hunting.
            receiver.ReceiveBytes(WallFrameSerializer.CreateFramePacket(trap), nowSeconds: 0.0);

            Assert.Equal(1, receiver.ValidPacketsReceived);
            Assert.True(receiver.CurrentFrame.ContentEquals(trap));

            // Now the hard case: knock the receiver out of step immediately
            // before the trap packet, so it is hunting when it meets the
            // 0xAA 0x55 sitting inside the payload.
            receiver.Reset();

            var stream = new List<byte> { 0xAA };  // a stray byte to break alignment
            stream.AddRange(WallFrameSerializer.CreateFramePacket(trap).Skip(1));

            // Then several ordinary packets, ending with a distinctive one.
            WallFrame settled = MakeFrame((3, 3));

            for (int i = 0; i < 3; i++)
            {
                stream.AddRange(WallFrameSerializer.CreateFramePacket(MakeFrame((0, 0), (i, i))));
            }

            stream.AddRange(WallFrameSerializer.CreateFramePacket(settled));

            receiver.ReceiveBytes(stream.ToArray(), nowSeconds: 0.0);

            // The receiver may have misread something in the confusion. What
            // must be true is that it ended up in step and showing the last
            // frame sent.
            Assert.True(
                receiver.CurrentFrame.ContentEquals(settled),
                "Receiver never recovered after being derailed by a payload containing the sync pair.");
        }

        [Fact]
        public void BlackoutCommand_ClearsTheWall()
        {
            var receiver = new VirtualWallReceiver();

            receiver.ReceiveBytes(
                WallFrameSerializer.CreateFramePacket(MakeFrame((0, 0), (1, 1), (2, 2))),
                nowSeconds: 0.0);

            Assert.Equal(3, receiver.CurrentFrame.CountLitCells());

            receiver.ReceiveBytes(WallFrameSerializer.CreateBlackoutPacket(), nowSeconds: 0.1);

            Assert.Equal(0, receiver.CurrentFrame.CountLitCells());
            Assert.Equal(1, receiver.BlackoutsReceived);
        }

        [Fact]
        public void HeartbeatCommand_LeavesTheWallAlone()
        {
            var receiver = new VirtualWallReceiver();
            WallFrame sent = MakeFrame((2, 2), (2, 3));

            receiver.ReceiveBytes(WallFrameSerializer.CreateFramePacket(sent), nowSeconds: 0.0);
            receiver.ReceiveBytes(WallFrameSerializer.CreateHeartbeatPacket(), nowSeconds: 0.1);

            Assert.Equal(1, receiver.HeartbeatsReceived);
            Assert.True(receiver.CurrentFrame.ContentEquals(sent));
        }

        [Fact]
        public void AnUnknownCommand_IsIgnoredWithoutBreakingTheStream()
        {
            // Old firmware meeting a newer app must not fall over. It should
            // skip what it does not understand and carry on.
            var receiver = new VirtualWallReceiver();

            byte[] unknown = WallFrameSerializer.CreatePacket(
                (PacketCommand)0x7F,
                new byte[WallFrameSerializer.PayloadLength]);

            WallFrame sent = MakeFrame((4, 4));

            var stream = new List<byte>();
            stream.AddRange(unknown);
            stream.AddRange(WallFrameSerializer.CreateFramePacket(sent));

            receiver.ReceiveBytes(stream.ToArray(), nowSeconds: 0.0);

            Assert.Equal(2, receiver.ValidPacketsReceived);
            Assert.Equal(0, receiver.ChecksumFailures);
            Assert.True(receiver.CurrentFrame.ContentEquals(sent));
        }

        [Fact]
        public void Watchdog_BlanksTheWallWhenTheAppGoesQuiet()
        {
            var receiver = new VirtualWallReceiver { WatchdogTimeoutSeconds = 1.0 };

            receiver.ReceiveBytes(
                WallFrameSerializer.CreateFramePacket(MakeFrame((0, 0), (0, 1))),
                nowSeconds: 10.0);

            Assert.Equal(2, receiver.CurrentFrame.CountLitCells());

            // Still within the timeout - the wall keeps its picture.
            receiver.Update(nowSeconds: 10.5);
            Assert.False(receiver.WatchdogTripped);
            Assert.Equal(2, receiver.CurrentFrame.CountLitCells());

            // Past the timeout - the wall goes dark by itself.
            receiver.Update(nowSeconds: 11.5);
            Assert.True(receiver.WatchdogTripped);
            Assert.Equal(0, receiver.CurrentFrame.CountLitCells());
        }

        [Fact]
        public void Watchdog_DoesNotFireBeforeAnythingHasBeenReceived()
        {
            // At power-on the Arduino has heard nothing yet. That is not the
            // same as having lost contact, and it should not count as a timeout.
            var receiver = new VirtualWallReceiver { WatchdogTimeoutSeconds = 1.0 };

            receiver.Update(nowSeconds: 500.0);

            Assert.False(receiver.WatchdogTripped);
        }

        [Fact]
        public void Heartbeat_KeepsTheWatchdogSatisfied()
        {
            // This is the whole point of the heartbeat: hold a still frame on
            // the wall during a stretch when no new frames are being sent.
            var receiver = new VirtualWallReceiver { WatchdogTimeoutSeconds = 1.0 };

            receiver.ReceiveBytes(
                WallFrameSerializer.CreateFramePacket(MakeFrame((1, 1))),
                nowSeconds: 0.0);

            // Tick along past the timeout, sending only heartbeats.
            for (double time = 0.5; time <= 5.0; time += 0.5)
            {
                receiver.ReceiveBytes(WallFrameSerializer.CreateHeartbeatPacket(), time);
            }

            Assert.False(receiver.WatchdogTripped);
            Assert.Equal(1, receiver.CurrentFrame.CountLitCells());
        }

        [Fact]
        public void AfterTheWatchdogFires_AGoodPacketBringsTheWallBack()
        {
            var receiver = new VirtualWallReceiver { WatchdogTimeoutSeconds = 1.0 };

            receiver.ReceiveBytes(
                WallFrameSerializer.CreateFramePacket(MakeFrame((0, 0))),
                nowSeconds: 0.0);

            receiver.Update(nowSeconds: 5.0);
            Assert.True(receiver.WatchdogTripped);

            WallFrame recovered = MakeFrame((3, 3), (3, 4));
            receiver.ReceiveBytes(WallFrameSerializer.CreateFramePacket(recovered), nowSeconds: 6.0);

            Assert.False(receiver.WatchdogTripped);
            Assert.True(receiver.CurrentFrame.ContentEquals(recovered));
        }
    }
}
