using System;
using LightWall.Core.Audio;
using LightWall.Core.Effects;
using LightWall.Core.Models;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the polish that stops the wall looking like static.
    ///
    /// The complaint that prompted these: with real music the right bands
    /// responded to the right frequencies, but individual bulbs flickered on and
    /// off for a frame or two constantly, giving the whole thing a jittery
    /// energy that was hard to read as connected to the song.
    ///
    /// There turned out to be several separate causes, and each of these tests
    /// pins down one of them.
    /// </summary>
    public class SmoothingTests
    {
        // ------------------------------------------------------------------
        // Chatter at row boundaries - the main cause
        // ------------------------------------------------------------------

        /// <summary>
        /// THE HEADLINE TEST.
        ///
        /// A level wandering by a hair around a row boundary must not make the
        /// bar flip back and forth. This was the single biggest source of the
        /// flickering: with five rows, a band sitting at 0.50 is exactly halfway
        /// between two and three rows, and plain rounding crosses that boundary
        /// on the smallest fluctuation.
        /// </summary>
        [Fact]
        public void ALevelHoveringAtARowBoundaryDoesNotMakeTheBarChatter()
        {
            var smoother = new BarHeightSmoother(barCount: 1, maximumHeight: 5);
            var random = new Random(1234);

            // Settle at the boundary first.
            smoother.GetHeight(0, 0.5);
            int settled = smoother.GetHeight(0, 0.5);

            int changes = 0;
            int previous = settled;

            // Now wobble by a thousandth either side, two hundred times.
            for (int i = 0; i < 200; i++)
            {
                double wobble = (random.NextDouble() - 0.5) * 0.002;
                int height = smoother.GetHeight(0, 0.5 + wobble);

                if (height != previous)
                {
                    changes++;
                    previous = height;
                }
            }

            Assert.Equal(0, changes);
        }

        [Fact]
        public void PlainRoundingWouldHaveChattered()
        {
            // Shows the problem is real rather than imagined, by doing what the
            // old code did and counting the flips.
            var random = new Random(1234);

            int changes = 0;
            int previous = (int)Math.Round(0.5 * 5, MidpointRounding.AwayFromZero);

            for (int i = 0; i < 200; i++)
            {
                double wobble = (random.NextDouble() - 0.5) * 0.002;
                int height = (int)Math.Round((0.5 + wobble) * 5, MidpointRounding.AwayFromZero);

                if (height != previous)
                {
                    changes++;
                    previous = height;
                }
            }

            Assert.True(
                changes > 20,
                $"Plain rounding only changed {changes} times; the test signal is not representative.");
        }

        [Fact]
        public void RealMovementStillMovesTheBar()
        {
            // Hysteresis must not make the wall unresponsive. A genuine change
            // in the music has to get through.
            var smoother = new BarHeightSmoother(barCount: 1, maximumHeight: 5);

            // Climbing all the way up, one clear step at a time.
            Assert.Equal(1, smoother.GetHeight(0, 0.2));
            Assert.Equal(2, smoother.GetHeight(0, 0.4));
            Assert.Equal(3, smoother.GetHeight(0, 0.6));
            Assert.Equal(5, smoother.GetHeight(0, 1.0));

            // And back down again.
            Assert.Equal(2, smoother.GetHeight(0, 0.4));
            Assert.Equal(0, smoother.GetHeight(0, 0.0));
        }

        [Fact]
        public void AskingTwiceGivesTheSameAnswer()
        {
            // The property the repeatability rule actually cares about: an
            // effect asked the same question twice must not answer differently.
            // Holding state is fine as long as it settles in one step.
            var smoother = new BarHeightSmoother(barCount: 7, maximumHeight: 5);

            for (int column = 0; column < 7; column++)
            {
                double level = 0.1 + (column * 0.12);

                int first = smoother.GetHeight(column, level);
                int second = smoother.GetHeight(column, level);
                int third = smoother.GetHeight(column, level);

                Assert.Equal(first, second);
                Assert.Equal(second, third);
            }
        }

        [Fact]
        public void HeightsStayWithinTheWall()
        {
            var smoother = new BarHeightSmoother(barCount: 7, maximumHeight: 5);
            var random = new Random(99);

            for (int i = 0; i < 2000; i++)
            {
                int column = random.Next(0, 7);
                int height = smoother.GetHeight(column, random.NextDouble() * 2.0 - 0.5);

                Assert.InRange(height, 0, 5);
            }
        }

        [Fact]
        public void BarsOutsideTheWallGiveZeroRatherThanCrashing()
        {
            var smoother = new BarHeightSmoother(barCount: 7, maximumHeight: 5);

            Assert.Equal(0, smoother.GetHeight(-1, 1.0));
            Assert.Equal(0, smoother.GetHeight(99, 1.0));
        }

        [Fact]
        public void AVeryQuietBandDoesNotLightItsFirstRow()
        {
            // A side benefit of the sticky boundaries, and a welcome one. Half a
            // row's worth of signal is not enough to switch a bulb on, so bands
            // carrying almost nothing stay properly dark instead of glowing at
            // one row and twitching.
            var smoother = new BarHeightSmoother(barCount: 1, maximumHeight: 5);

            Assert.Equal(0, smoother.GetHeight(0, 0.1));
        }

        [Fact]
        public void ResetDropsEveryBar()
        {
            var smoother = new BarHeightSmoother(barCount: 3, maximumHeight: 5);

            smoother.GetHeight(0, 1.0);
            smoother.GetHeight(1, 1.0);

            smoother.Reset();

            // After a reset, a mid level should be reached from zero rather than
            // held back by where the bar used to be.
            Assert.Equal(2, smoother.GetHeight(0, 0.4));
        }

        // ------------------------------------------------------------------
        // Quiet bands shimmering - the second cause
        // ------------------------------------------------------------------

        /// <summary>
        /// A band with nothing musical in it must stay dark.
        ///
        /// Without a gate, the automatic adjustment divides a tiny level by a
        /// tiny reference and the result swings wildly on noise nobody can hear.
        /// A band carrying nothing - the sub-bass on a track with no deep bass,
        /// say - would shimmer between one row and none several times a second.
        /// </summary>
        [Fact]
        public void ABandWithNothingInItStaysDarkRatherThanShimmering()
        {
            var gain = new AudioGainController();
            var random = new Random(555);

            int nonZero = 0;

            for (int i = 0; i < 500; i++)
            {
                // Inaudible noise, well below the gate.
                double level = random.NextDouble() * 0.03;

                if (gain.Normalise(level, 0.01) > 0.0)
                {
                    nonZero++;
                }
            }

            Assert.Equal(0, nonZero);
        }

        [Fact]
        public void TheGateDoesNotSuppressRealSound()
        {
            // It must only silence what is genuinely absent. Anything audible
            // has to get through.
            var gain = new AudioGainController();

            double settled = 0.0;

            for (int i = 0; i < 300; i++)
            {
                settled = gain.Normalise(0.3, 0.01);
            }

            Assert.True(settled > 0.5, $"Real sound was gated down to {settled:F2}.");
        }

        // ------------------------------------------------------------------
        // The shape across the wall - what gives it curves
        // ------------------------------------------------------------------

        /// <summary>
        /// Neighbouring columns should relate to each other, so the top edge
        /// reads as a rolling curve rather than seven independent spikes.
        /// </summary>
        [Fact]
        public void SmoothingRoundsOffASingleIsolatedSpike()
        {
            var jagged = new SpectrumAnalyser(48000) { Smoothing = 0.0 };
            var smooth = new SpectrumAnalyser(48000) { Smoothing = 1.0 };

            // A tone sitting squarely inside one band.
            float[] tone = MakeTone(9000.0, 2048, amplitude: 0.5);

            double[] jaggedBands = new double[FrequencyBands.Count];
            double[] smoothBands = new double[FrequencyBands.Count];

            for (int i = 0; i < 200; i++)
            {
                jagged.AddSamples(tone, 1);
                jaggedBands = jagged.Analyse(0.01);

                smooth.AddSamples(tone, 1);
                smoothBands = smooth.Analyse(0.01);
            }

            // With smoothing, the band next to the loud one should have picked
            // up something from it - the shoulder of the curve.
            Assert.True(
                smoothBands[5] > jaggedBands[5],
                $"Neighbour was {smoothBands[5]:F2} smoothed against {jaggedBands[5]:F2} raw; " +
                "the top edge would still read as a spike.");
        }

        [Fact]
        public void SmoothingDoesNotFlattenTheWallIntoOneBlur()
        {
            // The blend must stay well under half, or a band stops representing
            // its own frequencies and every column ends up the same.
            var analyser = new SpectrumAnalyser(48000) { Smoothing = 1.0 };

            float[] tone = MakeTone(9000.0, 2048, amplitude: 0.5);

            double[] bands = new double[FrequencyBands.Count];

            for (int i = 0; i < 200; i++)
            {
                analyser.AddSamples(tone, 1);
                bands = analyser.Analyse(0.01);
            }

            // The band containing the tone must still clearly lead.
            Assert.True(
                bands[6] > bands[0] * 2.0,
                $"At full smoothing the loud band was {bands[6]:F2} against {bands[0]:F2} " +
                "at the far end; the wall has blurred into one shape.");
        }

        [Fact]
        public void MoreSmoothingMeansASlowerFallBack()
        {
            var snappy = new SpectrumAnalyser(48000) { Smoothing = 0.0 };
            var flowing = new SpectrumAnalyser(48000) { Smoothing = 1.0 };

            float[] tone = MakeTone(200.0, 2048, amplitude: 0.6);

            for (int i = 0; i < 200; i++)
            {
                snappy.AddSamples(tone, 1);
                snappy.Analyse(0.01);

                flowing.AddSamples(tone, 1);
                flowing.Analyse(0.01);
            }

            // Then the music stops.
            double[] snappyAfter = new double[FrequencyBands.Count];
            double[] flowingAfter = new double[FrequencyBands.Count];

            for (int i = 0; i < 15; i++)
            {
                snappyAfter = snappy.AnalyseSilence(0.01);
                flowingAfter = flowing.AnalyseSilence(0.01);
            }

            Assert.True(
                flowingAfter[2] > snappyAfter[2],
                $"After silence, flowing was {flowingAfter[2]:F2} and snappy {snappyAfter[2]:F2}; " +
                "the smoothing control is not changing the fall-back time.");
        }

        [Fact]
        public void SmoothingIsClampedToASensibleRange()
        {
            var analyser = new SpectrumAnalyser(48000);

            analyser.Smoothing = 5.0;
            Assert.Equal(1.0, analyser.Smoothing);

            analyser.Smoothing = -3.0;
            Assert.Equal(0.0, analyser.Smoothing);
        }

        // ------------------------------------------------------------------
        // The effect as a whole
        // ------------------------------------------------------------------

        [Fact]
        public void TheEqStopsFlickeringWithARealisticWobblySignal()
        {
            // The end-to-end version of the headline test: a level wandering
            // slightly, as real music does, must not make bulbs switch on and
            // off dozens of times a second.
            var effect = new EqBumperEffect();
            var random = new Random(31337);

            var previous = new WallFrame();
            int changedFrames = 0;

            for (int i = 0; i < 300; i++)
            {
                var bands = new double[FrequencyBands.Count];

                for (int band = 0; band < bands.Length; band++)
                {
                    // Each band sits near a row boundary with a small wobble -
                    // the worst case for chatter.
                    bands[band] = 0.5 + ((random.NextDouble() - 0.5) * 0.02);
                }

                var features = new AudioFeatures(0.5, 0.5, 0.5, 0.5, bands, isSilent: false);
                var context = new EffectContext(i * 0.01, new EffectParameters(), 1, features, true);

                var frame = new WallFrame();
                effect.Render(context, frame);

                if (i > 0 && !frame.ContentEquals(previous))
                {
                    changedFrames++;
                }

                previous.CopyFrom(frame);
            }

            Assert.True(
                changedFrames <= 5,
                $"The wall changed on {changedFrames} of 300 frames despite a nearly steady " +
                "signal, which would read as flickering.");
        }

        /// <summary>
        /// Builds a pure tone at a given frequency.
        /// </summary>
        private static float[] MakeTone(double frequencyHz, int sampleCount, double amplitude)
        {
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequencyHz * i / 48000.0));
            }

            return samples;
        }
    }
}
