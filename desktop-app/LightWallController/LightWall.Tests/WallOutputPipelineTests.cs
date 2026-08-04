using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LightWall.Core.Effects;
using LightWall.Core.Engine;
using LightWall.Core.Models;
using LightWall.Core.Serialization;
using LightWall.Core.Transport;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the output pipeline: show clock, transport, output service.
    ///
    /// A NOTE ON TESTING THINGS THAT USE THREADS
    ///
    /// Most of these avoid starting the background threads at all, driving the
    /// pieces by hand instead. That is deliberate.
    ///
    /// A test that starts a thread and waits for something to happen is a test
    /// that sometimes fails on a busy machine, for no reason connected to the
    /// code. Those tests get ignored, then disabled, then deleted, and the
    /// coverage quietly disappears.
    ///
    /// The few tests below that genuinely need the threads - because the thing
    /// being checked IS the threading - are marked and given generous margins.
    /// </summary>
    public class WallOutputPipelineTests
    {
        /// <summary>
        /// A simple effect that lights a predictable number of bulbs, so tests
        /// can tell frames apart.
        /// </summary>
        private sealed class CountingEffect : IWallEffect
        {
            public string DisplayName => "Counting";

            public string Description => "Lights one more bulb each second, for testing.";

            public void Render(EffectContext context, WallFrame target)
            {
                target.Clear();

                int count = (context.GetStep(1.0) % 7) + 1;

                for (int column = 0; column < count; column++)
                {
                    target.SetCell(0, column, true);
                }
            }
        }

        /// <summary>
        /// Records every packet handed to it, so a test can inspect exactly what
        /// went out.
        /// </summary>
        private sealed class RecordingTransport : IWallTransport
        {
            public List<byte[]> Packets { get; } = new();

            public string Name => "Recording";

            public bool IsConnected { get; private set; }

            public void Connect() => IsConnected = true;

            public void Disconnect() => IsConnected = false;

            public void Send(byte[] packet) => Packets.Add((byte[])packet.Clone());

            public void Dispose() => Disconnect();
        }

        // ------------------------------------------------------------------
        // WallShowClock
        // ------------------------------------------------------------------

        [Fact]
        public void Clock_StartsInManualModeWithADarkWall()
        {
            using var clock = new WallShowClock();
            var frame = new WallFrame();

            clock.CopyCurrentFrameTo(frame);

            Assert.False(clock.IsPlaying);
            Assert.Equal(0, frame.CountLitCells());
        }

        [Fact]
        public void Clock_PlaysAnEffectAndReportsIt()
        {
            using var clock = new WallShowClock();
            var effect = new CountingEffect();

            clock.Modify(engine => engine.Play(effect));

            Assert.True(clock.IsPlaying);
            Assert.Same(effect, clock.ActiveEffect);
        }

        [Fact]
        public void Clock_HandsOutCopiesNotReferences()
        {
            // If the clock handed out its own frame, a caller could be reading it
            // while the tick thread rewrote it - producing a picture that is half
            // one frame and half the next.
            using var clock = new WallShowClock();
            clock.Modify(engine => engine.Play(new CountingEffect()));

            var first = new WallFrame();
            clock.CopyCurrentFrameTo(first);

            // Move the wall on, then confirm the earlier copy did not follow.
            clock.AdvanceManually(0.2);
            clock.AdvanceManually(0.2);
            clock.AdvanceManually(0.2);
            clock.AdvanceManually(0.2);
            clock.AdvanceManually(0.2);
            clock.AdvanceManually(0.2);

            var second = new WallFrame();
            clock.CopyCurrentFrameTo(second);

            Assert.False(
                first.ContentEquals(second),
                "The earlier copy changed when the wall moved on, so it was not a copy.");
        }

        [Fact]
        public void Clock_ModifyRejectsNothingToDo()
        {
            using var clock = new WallShowClock();

            Assert.Throws<ArgumentNullException>(() => clock.Modify(null!));
        }

        /// <summary>
        /// Uses the real background thread, because the thread is the point.
        /// </summary>
        [Fact]
        public void Clock_ActuallyAdvancesOnItsOwnThread()
        {
            using var clock = new WallShowClock();
            clock.Modify(engine => engine.Play(new CountingEffect()));

            var before = new WallFrame();
            clock.CopyCurrentFrameTo(before);

            clock.Start();
            Assert.True(clock.IsRunning);

            // Wait long enough that the effect must have moved on, with plenty
            // of margin for a busy machine.
            Thread.Sleep(1500);

            var after = new WallFrame();
            clock.CopyCurrentFrameTo(after);

            clock.Stop();

            Assert.False(clock.IsRunning);
            Assert.False(
                before.ContentEquals(after),
                "The wall did not change, so the tick thread was not advancing the engine.");
        }

        [Fact]
        public void Clock_CanBeStoppedAndStartedRepeatedly()
        {
            using var clock = new WallShowClock();

            clock.Start();
            clock.Start();   // starting twice should be harmless
            Assert.True(clock.IsRunning);

            clock.Stop();
            clock.Stop();    // stopping twice should be harmless
            Assert.False(clock.IsRunning);

            clock.Start();
            Assert.True(clock.IsRunning);
            clock.Stop();
        }

        // ------------------------------------------------------------------
        // LoopbackTransport
        // ------------------------------------------------------------------

        [Fact]
        public void Loopback_DeliversAFrameToTheVirtualWall()
        {
            using var loopback = new LoopbackTransport();
            loopback.Connect();

            var sent = new WallFrame();
            sent.SetCell(2, 3, true);
            sent.SetCell(0, 0, true);

            loopback.Send(WallFrameSerializer.CreateFramePacket(sent));

            var received = new WallFrame();
            loopback.CopyReceivedFrameTo(received);

            Assert.Equal(1, loopback.ValidPacketsReceived);
            Assert.Equal(0, loopback.ChecksumFailures);
            Assert.True(received.ContentEquals(sent));
        }

        [Fact]
        public void Loopback_IgnoresPacketsWhenNotConnected()
        {
            using var loopback = new LoopbackTransport();

            loopback.Send(WallFrameSerializer.CreateFramePacket(new WallFrame()));

            Assert.Equal(0, loopback.ValidPacketsReceived);
        }

        [Fact]
        public void Loopback_CorruptionFaultsAreCaughtByTheChecksum()
        {
            // Corrupt heavily so the test does not depend on luck, and use a
            // fixed seed so it behaves identically every run.
            using var loopback = new LoopbackTransport(faultSeed: 99)
            {
                ByteCorruptionProbability = 0.5
            };

            loopback.Connect();

            var frame = new WallFrame();
            frame.Fill();

            for (int i = 0; i < 50; i++)
            {
                loopback.Send(WallFrameSerializer.CreateFramePacket(frame));
            }

            Assert.True(loopback.BytesCorrupted > 0, "No bytes were corrupted, so the fault injection did nothing.");
            Assert.True(
                loopback.ChecksumFailures > 0,
                "Bytes were corrupted but no checksum failures were detected.");
        }

        [Fact]
        public void Loopback_RecoversAfterDroppedBytes()
        {
            // Drop bytes for a while, then stop, and confirm the virtual wall
            // catches up rather than staying permanently confused.
            using var loopback = new LoopbackTransport(faultSeed: 12345)
            {
                ByteDropProbability = 0.1
            };

            loopback.Connect();

            var noisy = new WallFrame();
            noisy.Randomize(new Random(1));

            for (int i = 0; i < 100; i++)
            {
                loopback.Send(WallFrameSerializer.CreateFramePacket(noisy));
            }

            Assert.True(loopback.BytesDropped > 0, "No bytes were dropped, so the fault injection did nothing.");

            // Now a clean stream.
            loopback.ByteDropProbability = 0.0;

            var settled = new WallFrame();
            settled.SetCell(4, 6, true);

            for (int i = 0; i < 20; i++)
            {
                loopback.Send(WallFrameSerializer.CreateFramePacket(settled));
            }

            var received = new WallFrame();
            loopback.CopyReceivedFrameTo(received);

            Assert.True(
                received.ContentEquals(settled),
                "The virtual wall never recovered after the dropped bytes stopped.");
        }

        // ------------------------------------------------------------------
        // WallOutputService
        // ------------------------------------------------------------------

        [Fact]
        public void Output_StartsDetached()
        {
            using var clock = new WallShowClock();
            using var output = new WallOutputService(clock);

            Assert.False(output.IsSending);
            Assert.Null(output.Transport);
        }

        [Fact]
        public void Output_AttachingConnectsTheTransport()
        {
            using var clock = new WallShowClock();
            using var output = new WallOutputService(clock);
            var transport = new RecordingTransport();

            output.Attach(transport);

            Assert.True(transport.IsConnected);
            Assert.Same(transport, output.Transport);
            Assert.True(output.IsSending);

            output.Detach();
        }

        [Fact]
        public void Output_DetachingSendsABlackoutThenDisconnects()
        {
            // The wall should go dark when output stops, rather than freezing on
            // whatever frame happened to be showing. Leaving bulbs lit with
            // nothing driving them is exactly what the firmware watchdog exists
            // to clean up, and it is better not to need rescuing.
            using var clock = new WallShowClock();
            using var output = new WallOutputService(clock);
            var transport = new RecordingTransport();

            output.Attach(transport);
            output.Detach();

            Assert.False(transport.IsConnected);
            Assert.NotEmpty(transport.Packets);

            byte[] last = transport.Packets[^1];

            Assert.True(WallFrameSerializer.TryParsePacket(last, out PacketCommand command, out _));
            Assert.Equal(PacketCommand.Blackout, command);
        }

        [Fact]
        public void Output_DisposingAlsoBlacksOut()
        {
            using var clock = new WallShowClock();
            var transport = new RecordingTransport();

            using (var output = new WallOutputService(clock))
            {
                output.Attach(transport);
            }

            Assert.False(transport.IsConnected);

            byte[] last = transport.Packets[^1];
            Assert.True(WallFrameSerializer.TryParsePacket(last, out PacketCommand command, out _));
            Assert.Equal(PacketCommand.Blackout, command);
        }

        [Fact]
        public void Output_SendImmediateBypassesTheRateLimit()
        {
            using var clock = new WallShowClock();
            using var output = new WallOutputService(clock);
            var transport = new RecordingTransport();

            output.Attach(transport);

            int before = transport.Packets.Count;
            output.SendImmediate(WallFrameSerializer.CreateBlackoutPacket());

            Assert.True(transport.Packets.Count > before);

            output.Detach();
        }

        [Fact]
        public void Output_AttachingASecondTransportReleasesTheFirst()
        {
            using var clock = new WallShowClock();
            using var output = new WallOutputService(clock);

            var first = new RecordingTransport();
            var second = new RecordingTransport();

            output.Attach(first);
            output.Attach(second);

            Assert.False(first.IsConnected);
            Assert.True(second.IsConnected);
            Assert.Same(second, output.Transport);

            output.Detach();
        }

        /// <summary>
        /// The rate limit is the one thing here that genuinely must be checked
        /// against real elapsed time, so this one uses the threads.
        ///
        /// The direction that matters is the upper bound. Sending a little
        /// slower than asked is harmless; sending faster means asking the relays
        /// for something they physically cannot do.
        /// </summary>
        [Fact]
        public void Output_NeverExceedsItsConfiguredRate()
        {
            using var clock = new WallShowClock();
            clock.Modify(engine => engine.Play(new CountingEffect()));
            clock.Start();

            using var output = new WallOutputService(clock) { OutputRateHz = 30.0 };
            var transport = new RecordingTransport();

            var stopwatch = Stopwatch.StartNew();
            output.Attach(transport);

            Thread.Sleep(2000);

            output.Detach();
            stopwatch.Stop();
            clock.Stop();

            double seconds = stopwatch.Elapsed.TotalSeconds;

            // One of the recorded packets is the blackout sent on detach, so
            // discount it before working out the rate.
            int frames = transport.Packets.Count - 1;
            double rate = frames / seconds;

            // A generous ceiling. The point is to catch a rate limiter that is
            // not limiting at all - if it were broken this would read in the
            // hundreds, because the loop wakes every 2 milliseconds.
            Assert.True(
                rate <= 40.0,
                $"Sent {rate:F1} packets per second, which exceeds the 30 requested.");

            // And confirm it is actually sending, not stalled.
            Assert.True(
                rate >= 10.0,
                $"Only sent {rate:F1} packets per second, which suggests output stalled.");
        }

        /// <summary>
        /// The end-to-end proof: an effect playing on the engine ends up
        /// correctly displayed on the virtual wall, having passed through
        /// packing, transmission, framing, checksum validation and unpacking.
        ///
        /// This is what "the virtual version fully working" means. Everything
        /// except the electrons.
        /// </summary>
        [Fact]
        public void EndToEnd_WhatTheEngineDrawsIsWhatTheVirtualWallShows()
        {
            using var clock = new WallShowClock();
            using var output = new WallOutputService(clock);
            using var loopback = new LoopbackTransport();

            var catalog = new EffectCatalog();

            clock.Modify(engine => engine.Play(catalog.FindByName("Checkerboard")!));
            output.Attach(loopback);

            // Give the send loop time to deliver several frames.
            Thread.Sleep(500);

            var expected = new WallFrame();
            clock.CopyCurrentFrameTo(expected);

            var actual = new WallFrame();
            loopback.CopyReceivedFrameTo(actual);

            Assert.True(loopback.ValidPacketsReceived > 0, "No packets reached the virtual wall.");
            Assert.Equal(0, loopback.ChecksumFailures);
            Assert.True(
                actual.ContentEquals(expected),
                "The virtual wall is not showing what the engine drew.");

            output.Detach();
        }
    }
}
