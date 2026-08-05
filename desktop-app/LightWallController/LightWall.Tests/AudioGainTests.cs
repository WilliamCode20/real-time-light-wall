using System;
using LightWall.Core.Audio;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the automatic volume adjustment.
    ///
    /// The claim being checked is a specific one: the wall should behave the
    /// same whether the computer's volume is at half or full. That is easy to
    /// state and easy to get subtly wrong, so it is worth proving directly by
    /// feeding the same music at two different volumes and comparing.
    /// </summary>
    public class AudioGainTests
    {
        /// <summary>
        /// Runs a stretch of steady sound through the controller and returns
        /// where the output settles.
        /// </summary>
        private static double SettleAt(
            AudioGainController gain,
            double level,
            double seconds = 2.0,
            double step = 0.01)
        {
            double result = 0.0;
            int steps = (int)(seconds / step);

            for (int i = 0; i < steps; i++)
            {
                result = gain.Normalise(level, step);
            }

            return result;
        }

        /// <summary>
        /// THE HEADLINE TEST.
        ///
        /// The same music at two very different system volumes should end up
        /// driving the wall to nearly the same place.
        /// </summary>
        [Fact]
        public void TheSameMusicAtHalfVolumeReachesTheSameHeight()
        {
            var atFullVolume = new AudioGainController();
            var atHalfVolume = new AudioGainController();

            double loud = SettleAt(atFullVolume, 0.8);
            double quiet = SettleAt(atHalfVolume, 0.4);

            Assert.Equal(loud, quiet, precision: 2);
        }

        [Fact]
        public void EvenVeryQuietMusicStillFillsTheWall()
        {
            // Someone with their volume right down should still get a working
            // light show, as long as it is above the noise floor.
            var gain = new AudioGainController();

            double settled = SettleAt(gain, 0.2);

            Assert.True(
                settled > 0.8,
                $"Quiet music only reached {settled:F2}; expected it to fill most of the wall.");
        }

        [Fact]
        public void SilenceIsNotAmplifiedIntoALightShow()
        {
            // The reason the noise floor exists. Without it, dividing a tiny
            // level by a tiny reference would blow silence up to full scale and
            // the wall would strobe to nothing at all between tracks.
            var gain = new AudioGainController();

            // Loud music first, so there is a high reference to decay from.
            SettleAt(gain, 0.9);

            // Then a long silence.
            double settled = SettleAt(gain, 0.0, seconds: 30.0);

            Assert.Equal(0.0, settled, precision: 6);
        }

        [Fact]
        public void RoomHissDoesNotBecomeFullScale()
        {
            // Something barely above nothing should stay barely above nothing,
            // rather than being wound up to fill the wall.
            var gain = new AudioGainController { MinimumReference = 0.15 };

            double settled = SettleAt(gain, 0.02, seconds: 30.0);

            Assert.True(settled < 0.2, $"Near-silence reached {settled:F2}, which is far too high.");
        }

        [Fact]
        public void ASuddenLoudMomentDoesNotClipForSeconds()
        {
            // The reference jumps up instantly rather than easing, so a track
            // that suddenly gets much louder does not sit pinned at full height
            // while the adjustment catches up.
            var gain = new AudioGainController();

            SettleAt(gain, 0.3);

            // One much louder reading.
            double afterJump = gain.Normalise(0.95, 0.01);

            Assert.True(afterJump <= 1.0);

            // And the reference should already have moved up to meet it.
            Assert.True(
                gain.Reference >= 0.9,
                $"Reference was only {gain.Reference:F2} after a loud moment.");
        }

        [Fact]
        public void TheReferenceForgetsLoudMusicOverTime()
        {
            // Otherwise one loud moment would suppress the wall for the rest of
            // the night.
            var gain = new AudioGainController { ReferenceDecaySeconds = 1.0 };

            gain.Normalise(1.0, 0.01);
            Assert.True(gain.Reference > 0.9);

            SettleAt(gain, 0.0, seconds: 10.0);

            Assert.True(
                gain.Reference <= gain.MinimumReference + 0.01,
                $"Reference was still {gain.Reference:F2} after ten seconds of quiet.");
        }

        [Fact]
        public void LouderMusicStillReadsHigherThanQuieterMusicMomentToMoment()
        {
            // The adjustment works over seconds, so within a single moment the
            // ordering must still hold — otherwise a snare would not read louder
            // than the gap before it.
            var gain = new AudioGainController();

            SettleAt(gain, 0.5);

            double quietMoment = gain.Normalise(0.2, 0.01);
            double loudMoment = gain.Normalise(0.5, 0.01);

            Assert.True(loudMoment > quietMoment);
        }

        [Fact]
        public void SensitivityMakesTheWallBumpHarder()
        {
            var gentle = new AudioGainController { Gain = 1.0 };
            var punchy = new AudioGainController { Gain = 2.0 };

            // A level well below the reference, so neither is already at the top.
            gentle.Normalise(1.0, 0.01);
            punchy.Normalise(1.0, 0.01);

            double gentleResult = gentle.Normalise(0.4, 0.01);
            double punchyResult = punchy.Normalise(0.4, 0.01);

            Assert.True(punchyResult > gentleResult);
        }

        [Fact]
        public void ContrastSeparatesQuietFromLoud()
        {
            // Higher contrast pushes middling levels further down while leaving
            // the loud ones high, which is what spreads the bars over more of
            // the wall.
            var flat = new AudioGainController { Contrast = 1.0 };
            var punchy = new AudioGainController { Contrast = 2.5 };

            flat.Normalise(1.0, 0.01);
            punchy.Normalise(1.0, 0.01);

            double flatMid = flat.Normalise(0.5, 0.01);
            double punchyMid = punchy.Normalise(0.5, 0.01);

            Assert.True(
                punchyMid < flatMid,
                $"Contrast made no difference: {punchyMid:F2} vs {flatMid:F2}.");
        }

        [Fact]
        public void OutputNeverLeavesTheZeroToOneRange()
        {
            // Everything downstream assumes this, including the bar-height
            // calculation.
            var gain = new AudioGainController { Gain = 3.0 };
            var random = new Random(4242);

            for (int i = 0; i < 2000; i++)
            {
                double level = random.NextDouble();
                double result = gain.Normalise(level, 0.01);

                Assert.InRange(result, 0.0, 1.0);
                Assert.False(double.IsNaN(result));
            }
        }

        [Fact]
        public void ResetForgetsEverything()
        {
            var gain = new AudioGainController();

            gain.Normalise(1.0, 0.01);
            Assert.True(gain.Reference > 0.0);

            gain.Reset();

            Assert.Equal(0.0, gain.Reference);
        }

        /// <summary>
        /// The test that actually matters for how the wall looks.
        ///
        /// A steady tone correctly reads as "constantly at the recent maximum",
        /// so it pins the bars at full height — which is honest but tells us
        /// nothing about music. Real music has transients: a hit, then a gap,
        /// then another hit. Those are what the bars should bounce to.
        ///
        /// This feeds a beat pattern through the whole chain — decibel mapping,
        /// attack and release smoothing, then the automatic gain — and checks
        /// the bars really do swing rather than sitting pinned near the top.
        /// </summary>
        [Fact]
        public void ABeatPatternMakesTheBarsSwingAcrossSeveralRows()
        {
            var tracker = new AudioLevelTracker();

            const double stepSeconds = 0.005;
            const double beatSeconds = 0.5;      // 120 beats per minute
            const double hitSeconds = 0.04;      // how long each hit lasts

            const double hitLoudness = 0.25;     // a kick drum
            const double gapLoudness = 0.04;     // the sound between hits

            double lowest = 1.0;
            double highest = 0.0;
            double elapsed = 0.0;

            // Run for a while so the automatic gain settles, then measure over
            // the last few seconds.
            while (elapsed < 12.0)
            {
                double intoBeat = elapsed % beatSeconds;
                double rms = intoBeat < hitSeconds ? hitLoudness : gapLoudness;

                AudioFeatures features = tracker.Update(rms, rms, stepSeconds);

                if (elapsed > 8.0)
                {
                    lowest = Math.Min(lowest, features.NormalisedLevel);
                    highest = Math.Max(highest, features.NormalisedLevel);
                }

                elapsed += stepSeconds;
            }

            // Convert to bar heights the way the effect does, so the assertion
            // is about what is actually seen on the wall.
            int lowestBar = (int)Math.Round(lowest * 5, MidpointRounding.AwayFromZero);
            int highestBar = (int)Math.Round(highest * 5, MidpointRounding.AwayFromZero);

            Assert.True(
                highestBar >= 4,
                $"Beats only reached {highestBar} rows; expected them to nearly fill the wall.");

            Assert.True(
                highestBar - lowestBar >= 2,
                $"Bars only swung between {lowestBar} and {highestBar} rows, which would " +
                "barely read as movement.");
        }

        [Fact]
        public void TheTrackerProducesBothAbsoluteAndAdjustedLevels()
        {
            // Level is absolute and follows the volume knob; NormalisedLevel is
            // adjusted and does not. Both are carried so the meter can show what
            // is really happening alongside what drives the wall.
            var tracker = new AudioLevelTracker();

            AudioFeatures features = tracker.Update(rms: 0.1, peak: 0.2, deltaSeconds: 0.01);

            Assert.InRange(features.Level, 0.0, 1.0);
            Assert.InRange(features.NormalisedLevel, 0.0, 1.0);
            Assert.Equal(0.1, features.Rms, precision: 6);
        }
    }
}
