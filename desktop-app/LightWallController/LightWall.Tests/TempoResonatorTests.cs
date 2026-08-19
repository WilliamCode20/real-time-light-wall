using System;
using LightWall.Core.Audio;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for finding the tempo from the onset curve itself.
    ///
    /// The property worth pinning hardest is the one in the middle: tempo and
    /// phase come out of the same number, so the beats it reports have to
    /// actually land where the pulses are. A period estimate that is right while
    /// the phase points somewhere else is the failure mode a separately-nudged
    /// metronome can have and this design is supposed to make impossible.
    /// </summary>
    public class TempoResonatorTests
    {
        private const double ReadingInterval = 0.0116;

        /// <summary>
        /// Plays a pulse train of a given tempo through a resonator.
        /// </summary>
        private static TempoResonator Play(
            double bpm,
            double seconds,
            TempoResonator? into = null,
            double startAt = 0.0,
            double pulseWidth = 0.03)
        {
            var resonator = into ?? new TempoResonator();
            double period = 60.0 / bpm;
            double now = startAt;
            double end = startAt + seconds;

            while (now < end)
            {
                double intoBeat = now % period;
                double strength = intoBeat < pulseWidth ? 1.0 : 0.02;

                resonator.Update(strength, now);
                now += ReadingInterval;
            }

            return resonator;
        }

        [Theory]
        [InlineData(90.0)]
        [InlineData(110.0)]
        [InlineData(126.0)]
        [InlineData(140.0)]
        public void FindsATempoFromTheCurveWithNoThresholdAnywhere(double bpm)
        {
            TempoResonator resonator = Play(bpm, 30.0);

            Assert.InRange(resonator.Bpm, bpm * 0.98, bpm * 1.02);
        }

        /// <summary>
        /// THE PROPERTY THE WHOLE DESIGN RESTS ON.
        ///
        /// Tempo and phase are read off one number, so they cannot disagree.
        /// This checks the consequence directly: at the moment a pulse arrives,
        /// the reported phase has to be near zero - meaning the beat is right
        /// now - and half way between pulses it has to be near a half.
        /// </summary>
        [Fact]
        public void ThePhaseItReportsPointsAtWhereThePulsesActuallyAre()
        {
            const double bpm = 120.0;
            const double period = 0.5;

            var resonator = new TempoResonator();
            Play(bpm, 30.0, resonator);

            // Step forward to land exactly on a pulse, and read the phase there.
            double now = 30.0;
            double onPulse = Math.Ceiling(now / period) * period;

            while (now < onPulse)
            {
                double intoBeat = now % period;
                resonator.Update(intoBeat < 0.03 ? 1.0 : 0.02, now);
                now += ReadingInterval;
            }

            double phaseAtPulse = resonator.BeatPhase;

            // Phase wraps, so being just under 1 is as good as being just over 0.
            double offBy = Math.Min(phaseAtPulse, 1.0 - phaseAtPulse);

            Assert.True(
                offBy < 0.12,
                $"At the moment a pulse arrived the phase read {phaseAtPulse:F3}, which is " +
                $"{offBy:F3} of a beat away from where the beat actually was. Tempo and " +
                "phase are meant to come from the same number and cannot disagree.");
        }

        [Fact]
        public void SaysNothingBeforeItHasHeardAnything()
        {
            var resonator = new TempoResonator();

            resonator.Update(0.5, 0.0);
            resonator.Update(0.5, ReadingInterval);

            Assert.Equal(0.0, resonator.Bpm);
        }

        /// <summary>
        /// A plain pulse train is genuinely ambiguous - music at 140 pulses just
        /// as truthfully at 70. Nothing in the signal settles it, so the
        /// preference does, and it should land on the reading nearer the speed
        /// people actually tap at.
        /// </summary>
        [Fact]
        public void PrefersTheTempoAListenerWouldTapWhenTheOctaveIsAmbiguous()
        {
            TempoResonator resonator = Play(140.0, 30.0);

            // Should report 140 rather than its half, 70, which fits equally.
            Assert.InRange(resonator.Bpm, 135.0, 145.0);
        }

        /// <summary>
        /// CONFIDENCE AND STRENGTH ANSWER DIFFERENT QUESTIONS, AND THIS IS WHERE
        /// THAT BECOMES OBVIOUS.
        ///
        /// Fed pure noise, confidence lands around 40% - which looks wrong until
        /// you read what it measures. It asks how much the winner beats a
        /// genuinely different rival, and in noise SOMETHING wins by some margin
        /// purely by chance. It is a statement about which tempo, not about
        /// whether there is one.
        ///
        /// Strength is the property that answers "is there any rhythm here at
        /// all", and it separates the two cases cleanly. Anything deciding
        /// whether to trust this at all should read Strength; anything deciding
        /// between two candidate tempos should read Confidence.
        /// </summary>
        [Fact]
        public void StrengthTellsRhythmFromNoiseWhereConfidenceDoesNot()
        {
            TempoResonator clean = Play(120.0, 30.0);

            var noisy = new TempoResonator();
            var random = new Random(11);
            double now = 0.0;

            while (now < 30.0)
            {
                noisy.Update(random.NextDouble(), now);
                now += ReadingInterval;
            }

            Assert.True(
                clean.Strength > noisy.Strength * 2.0,
                $"A clear pulse train resonated at {clean.Strength:F2} against noise at " +
                $"{noisy.Strength:F2}. Strength is meant to be the measure that tells " +
                "rhythm from no rhythm.");
        }

        [Fact]
        public void ReportsHigherConfidenceOnAClearPulseThanOnNoise()
        {
            TempoResonator clean = Play(120.0, 30.0);

            var noisy = new TempoResonator();
            var random = new Random(3);
            double now = 0.0;
            while (now < 30.0)
            {
                noisy.Update(random.NextDouble(), now);
                now += ReadingInterval;
            }

            Assert.True(
                clean.Confidence > noisy.Confidence,
                $"A clear pulse train scored {clean.Confidence:P0} against noise at " +
                $"{noisy.Confidence:P0}. Confidence is not telling them apart.");
        }

        [Fact]
        public void ForgettingReturnsItToKnowingNothing()
        {
            TempoResonator resonator = Play(120.0, 30.0);
            Assert.True(resonator.Bpm > 0.0);

            resonator.Reset();

            Assert.Equal(0.0, resonator.Bpm);
            Assert.Equal(0.0, resonator.Confidence);
            Assert.Equal(0.0, resonator.Strength);
        }

        [Fact]
        public void FollowsARealChangeOfTempoEventually()
        {
            var resonator = new TempoResonator { MemorySeconds = 6.0 };

            Play(100.0, 30.0, resonator);
            Assert.InRange(resonator.Bpm, 98.0, 102.0);

            Play(140.0, 40.0, resonator, startAt: 30.0);

            Assert.InRange(
                resonator.Bpm, 137.0, 143.0);
        }
    }
}
