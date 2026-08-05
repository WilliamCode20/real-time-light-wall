using System;
using LightWall.Core.Audio;
using LightWall.Core.Effects;
using LightWall.Core.Models;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the metronome that runs at the estimated tempo.
    ///
    /// Two things matter here and both are checked directly: that it pulses at
    /// the rate it was told, and that it keeps going through a quiet passage
    /// where onset detection finds nothing.
    /// </summary>
    public class TempoPulseTests
    {
        /// <summary>
        /// Runs the clock for a stretch of time and counts the pulses.
        /// </summary>
        private static int CountPulses(BeatClock clock, double bpm, double seconds, double step = 0.005)
        {
            int start = clock.PulseCount;

            for (double t = 0.0; t < seconds; t += step)
            {
                clock.Update(step, bpm);
            }

            return clock.PulseCount - start;
        }

        [Theory]
        [InlineData(60.0, 10.0, 10)]
        [InlineData(120.0, 10.0, 20)]
        [InlineData(128.0, 30.0, 64)]
        public void TheClockPulsesAtTheTempoItWasGiven(double bpm, double seconds, int expected)
        {
            var clock = new BeatClock();

            int pulses = CountPulses(clock, bpm, seconds);

            // Within one, since the run may start or end mid-beat.
            Assert.InRange(pulses, expected - 1, expected + 1);
        }

        [Fact]
        public void NoTempoMeansNoPulsing()
        {
            var clock = new BeatClock();

            Assert.Equal(0, CountPulses(clock, bpm: 0.0, seconds: 10.0));
            Assert.False(clock.IsPulsing);
        }

        [Fact]
        public void TheClockKeepsCountingWithNoBeatsBeingHeard()
        {
            // The whole reason this exists. A breakdown with nothing playing
            // should still pulse in time - Update is called, SyncToDetectedBeat
            // never is.
            var clock = new BeatClock();

            Assert.InRange(CountPulses(clock, bpm: 120.0, seconds: 16.0), 31, 33);
        }

        [Fact]
        public void SyncingNudgesTowardTheBeatRatherThanJumping()
        {
            // Snapping would make the pulse lurch every time detection was
            // slightly off, which is often. Nudging pulls it into alignment over
            // several beats while shrugging off any single bad one.
            var clock = new BeatClock();
            clock.Update(0.25, 120.0);   // half a beat in, so phase is 0.5

            double before = clock.Phase;
            clock.SyncToDetectedBeat();

            Assert.NotEqual(before, clock.Phase);

            // Moved, but nowhere near all the way.
            Assert.True(
                Math.Abs(clock.Phase - before) < 0.3,
                $"Phase jumped from {before:F2} to {clock.Phase:F2}; that is a lurch, not a nudge.");
        }

        [Fact]
        public void RepeatedSyncingPullsTheClockIntoAlignment()
        {
            var clock = new BeatClock();
            clock.Update(0.1, 120.0);

            // A run of detections all saying "the beat is now".
            for (int i = 0; i < 25; i++)
            {
                clock.SyncToDetectedBeat();
            }

            // Should have settled close to the start of a beat.
            Assert.True(
                clock.Phase < 0.05 || clock.Phase > 0.95,
                $"Phase settled at {clock.Phase:F2} rather than near a beat.");
        }

        [Fact]
        public void ALongStallDoesNotJamThePulseOn()
        {
            // A laptop waking up can report that seconds passed in one update.
            // Without the wrapping loop the phase would sit above 1 and the wall
            // would stay lit.
            var clock = new BeatClock();

            clock.Update(5.0, 120.0);

            Assert.InRange(clock.Phase, 0.0, 1.0);
        }

        // ------------------------------------------------------------------
        // Holding the tempo through quiet passages
        // ------------------------------------------------------------------

        /// <summary>
        /// THE ONE THAT MATTERS FOR BREAKDOWNS.
        ///
        /// An earlier version wiped the tempo after three seconds without beats,
        /// which meant exactly the passages where holding the beat matters most
        /// left the wall dead until the drums came back.
        /// </summary>
        [Fact]
        public void TheTempoSurvivesALongQuietSection()
        {
            var estimator = new TempoEstimator();

            for (int i = 0; i < 12; i++)
            {
                estimator.AddBeat(i * 0.5);
            }

            double established = estimator.Bpm;
            Assert.Equal(120.0, established, precision: 0);

            // Sixteen seconds of nothing - eight bars at 120.
            estimator.Update(nowSeconds: 5.5 + 16.0);

            Assert.Equal(established, estimator.Bpm, precision: 0);
        }

        [Fact]
        public void ConfidenceFallsDuringTheQuietWhileTheTempoHolds()
        {
            // The distinction worth reporting: "120, measured just now" against
            // "still 120, but nothing has confirmed it for a while".
            var estimator = new TempoEstimator();

            for (int i = 0; i < 12; i++)
            {
                estimator.AddBeat(i * 0.5);
            }

            double freshConfidence = estimator.Confidence;

            estimator.Update(nowSeconds: 5.5 + 10.0);

            Assert.True(
                estimator.Confidence < freshConfidence,
                "Confidence did not fall during a quiet stretch.");

            Assert.True(estimator.Bpm > 0.0, "The tempo was dropped rather than held.");
        }

        [Fact]
        public void AGenuinelyFinishedTrackIsEventuallyForgotten()
        {
            // Held indefinitely, a stale number would sit there looking current.
            var estimator = new TempoEstimator();

            for (int i = 0; i < 12; i++)
            {
                estimator.AddBeat(i * 0.5);
            }

            estimator.Update(nowSeconds: 5.5 + estimator.ForgetAfterSeconds + 1.0);

            Assert.Equal(0.0, estimator.Bpm);
        }

        // ------------------------------------------------------------------
        // The effect
        // ------------------------------------------------------------------

        private static WallFrame RenderPulse(double phase, double bpm = 120.0, bool audioActive = true)
        {
            var features = new AudioFeatures(
                0.5, 0.5, 0.5, 0.5,
                new double[FrequencyBands.Count],
                isSilent: false,
                tempoBpm: bpm,
                beatPhase: phase);

            var context = new EffectContext(0.0, new EffectParameters(), 1, features, audioActive);

            var frame = new WallFrame();
            new TempoPulseEffect().Render(context, frame);
            return frame;
        }

        [Fact]
        public void ThePulseLightsTheWallAtTheStartOfEachBeat()
        {
            Assert.Equal(35, RenderPulse(0.0).CountLitCells());
            Assert.Equal(35, RenderPulse(0.15).CountLitCells());
        }

        [Fact]
        public void ThePulseGoesOutForTheRestOfTheBeat()
        {
            Assert.Equal(0, RenderPulse(0.5).CountLitCells());
            Assert.Equal(0, RenderPulse(0.95).CountLitCells());
        }

        [Fact]
        public void ThePulseWaitsUntilATempoIsKnown()
        {
            // A single row says "running, waiting" rather than pretending to
            // have found a beat.
            Assert.Equal(WallFrame.Columns, RenderPulse(0.0, bpm: 0.0).CountLitCells());
            Assert.Equal(WallFrame.Columns, RenderPulse(0.0, audioActive: false).CountLitCells());
        }

        [Fact]
        public void ThePulseKeepsTheSameFeelAtAnyTempo()
        {
            // The pulse width is a fraction of a beat rather than a fixed number
            // of seconds, so it looks the same fast or slow. A fixed 100
            // milliseconds would be a blink at 90 BPM and nearly solid at 180.
            Assert.Equal(35, RenderPulse(0.1, bpm: 90.0).CountLitCells());
            Assert.Equal(35, RenderPulse(0.1, bpm: 180.0).CountLitCells());

            Assert.Equal(0, RenderPulse(0.5, bpm: 90.0).CountLitCells());
            Assert.Equal(0, RenderPulse(0.5, bpm: 180.0).CountLitCells());
        }
    }
}
