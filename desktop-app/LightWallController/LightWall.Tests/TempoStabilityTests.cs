using System;
using LightWall.Core.Audio;
using LightWall.Core.Effects;
using LightWall.Core.Models;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for "has the tempo stopped moving", and for the beat source that
    /// waits until it has.
    ///
    /// The measure exists because confidence turned out to be the wrong question
    /// to ask about whether an answer is worth relying on. On a real recording
    /// the tempo was right for most of three minutes while confidence sat
    /// between 35% and 56%, never once clearing the half needed to build any
    /// trust - so the estimate had no memory and wandered off the right answer
    /// four separate times.
    /// </summary>
    public class TempoStabilityTests
    {
        /// <summary>
        /// Feeds an estimator a steady stream of beats at a given tempo.
        /// </summary>
        private static double PlaySteady(TempoEstimator tempo, double bpm, double seconds, double startAt = 0.0)
        {
            double interval = 60.0 / bpm;
            double now = startAt;
            double end = startAt + seconds;

            while (now < end)
            {
                tempo.AddBeat(now);
                tempo.Update(now);
                now += interval;
            }

            return now;
        }

        [Fact]
        public void AnEstimateThatHasNotSettledScoresNothing()
        {
            var tempo = new TempoEstimator();

            // Barely enough to report anything at all.
            PlaySteady(tempo, 120, 2.5);

            Assert.True(
                tempo.Stability < 1.0,
                $"Two seconds in, stability was already {tempo.Stability:F2}.");
        }

        [Fact]
        public void HoldingTheSameTempoBuildsStability()
        {
            var tempo = new TempoEstimator();
            PlaySteady(tempo, 120, 25.0);

            Assert.Equal(1.0, tempo.Stability, precision: 6);
            Assert.InRange(tempo.Bpm, 118.0, 122.0);
        }

        /// <summary>
        /// THE CASE THIS WAS BUILT FOR.
        ///
        /// A steadily held tempo has to be able to earn trust even when
        /// confidence never reaches the bar - because on real music it often
        /// does not, and without trust the estimate has nothing to resist being
        /// shoved around with.
        /// </summary>
        [Fact]
        public void HoldingSteadyEarnsTrustEvenWhenConfidenceNeverClearsTheBar()
        {
            var tempo = new TempoEstimator();
            var random = new Random(7);

            // A steady 120 with TWO off-beat sounds scattered between every pair
            // of beats, so only a third of what is heard lands on the beat. The
            // tempo is plain; the share of sounds agreeing with it is not, which
            // is what holds confidence down.
            //
            // One off-beat sound is not enough - that lands confidence at
            // exactly a half, right on the bar, and the guard below catches it.
            double interval = 0.5;
            double now = 0.0;

            while (now < 40.0)
            {
                tempo.AddBeat(now);
                tempo.AddBeat(now + (interval * (0.22 + (random.NextDouble() * 0.16))));
                tempo.AddBeat(now + (interval * (0.58 + (random.NextDouble() * 0.16))));
                tempo.Update(now + interval);
                now += interval;
            }

            Assert.InRange(tempo.Bpm, 117.0, 123.0);

            Assert.True(
                tempo.Confidence < 0.5,
                $"This material was meant to keep confidence below the bar, but it " +
                $"reached {tempo.Confidence:P0}, so the test is not exercising what it claims.");

            Assert.True(
                tempo.Trust > 0.3,
                $"Trust was only {tempo.Trust:F2} after forty seconds of a rock-steady " +
                "tempo. Holding still has to be a route to trust, or material whose " +
                "confidence never clears the bar has nothing to resist being shoved with.");
        }

        [Fact]
        public void AJumpToADifferentTempoStartsTheStretchAgain()
        {
            var tempo = new TempoEstimator();
            double now = PlaySteady(tempo, 120, 25.0);

            Assert.Equal(1.0, tempo.Stability, precision: 6);

            // Something quite different, long enough to be adopted.
            tempo.Reset();
            PlaySteady(tempo, 150, 3.0, now);

            Assert.True(
                tempo.Stability < 1.0,
                $"After changing tempo, stability was still {tempo.Stability:F2}.");
        }

        // ------------------------------------------------------------------
        // The automatic beat source
        // ------------------------------------------------------------------

        private static EffectContext Context(BeatSource source, double stability, int beats, int pulses)
        {
            var audio = new AudioFeatures(
                0.2, 0.3, 0.4, 0.5,
                new double[FrequencyBands.Count],
                isSilent: false,
                secondsSinceBeat: 0.1,
                beatCount: beats,
                tempoBpm: 120.0,
                tempoConfidence: 0.4,
                secondsSincePulse: 0.2,
                pulseCount: pulses,
                beatPhase: 0.3,
                tempoStability: stability);

            return new EffectContext(
                1.0, new EffectParameters { BeatSource = source }, 1234, audio, isAudioActive: true);
        }

        [Fact]
        public void AutomaticFollowsWhatWasHeardWhileTheTempoIsStillMoving()
        {
            EffectContext context = Context(BeatSource.Automatic, stability: 0.4, beats: 11, pulses: 77);

            Assert.Equal(11, context.BeatCount);
            Assert.Equal(0.1, context.SecondsSinceBeat, precision: 6);
        }

        [Fact]
        public void AutomaticSwitchesToTheMetronomeOnceTheTempoHasSettled()
        {
            EffectContext context = Context(BeatSource.Automatic, stability: 1.0, beats: 11, pulses: 77);

            Assert.Equal(77, context.BeatCount);
            Assert.Equal(0.2, context.SecondsSinceBeat, precision: 6);
        }

        [Fact]
        public void TheTwoManualChoicesIgnoreStabilityEntirely()
        {
            // Whichever way stability happens to be sitting, an explicit choice
            // is an explicit choice. Both are diagnostic tools and would be
            // useless if they quietly did something else.
            Assert.Equal(11, Context(BeatSource.Detected, 1.0, 11, 77).BeatCount);
            Assert.Equal(11, Context(BeatSource.Detected, 0.0, 11, 77).BeatCount);

            Assert.Equal(77, Context(BeatSource.Tempo, 1.0, 11, 77).BeatCount);
            Assert.Equal(77, Context(BeatSource.Tempo, 0.0, 11, 77).BeatCount);
        }

        [Fact]
        public void AutomaticIsCarriedThroughWhenParametersAreCopied()
        {
            var parameters = new EffectParameters { BeatSource = BeatSource.Automatic };

            // A setting missing from Clone silently reverts to its default and
            // shows up as "it sometimes ignores the switch".
            Assert.Equal(BeatSource.Automatic, parameters.Clone().BeatSource);
        }
    }
}
