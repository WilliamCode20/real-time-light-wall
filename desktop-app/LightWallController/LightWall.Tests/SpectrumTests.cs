using System;
using LightWall.Core.Audio;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the frequency analysis.
    ///
    /// These are the most satisfying tests in the project, because they check
    /// against answers that come from mathematics rather than from our own code.
    /// Feed in a pure 100 Hz tone and the energy must appear at 100 Hz - if it
    /// does not, something is wrong, and no amount of the code agreeing with
    /// itself would reveal that.
    ///
    /// None of it needs a sound card, Windows, or anything playing. That is the
    /// entire reason the analysis lives in Core rather than beside the WASAPI
    /// plumbing.
    /// </summary>
    public class SpectrumTests
    {
        private const int SampleRate = 48000;

        /// <summary>
        /// Builds a pure tone at a given frequency.
        /// </summary>
        private static float[] MakeTone(double frequencyHz, int sampleCount, double amplitude = 0.5)
        {
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequencyHz * i / SampleRate));
            }

            return samples;
        }

        // ------------------------------------------------------------------
        // The transform itself
        // ------------------------------------------------------------------

        /// <summary>
        /// THE FOUNDATIONAL TEST.
        ///
        /// A pure tone must show up as energy at that frequency and nowhere
        /// else. If this passes, the transform is doing what its name says.
        /// </summary>
        [Fact]
        public void APureToneAppearsAtItsOwnFrequency()
        {
            const int size = 1024;
            const double toneHz = 1500.0;

            var real = new double[size];
            var imaginary = new double[size];

            float[] tone = MakeTone(toneHz, size);

            for (int i = 0; i < size; i++)
            {
                real[i] = tone[i];
            }

            FourierTransform.Forward(real, imaginary);

            // Find which output came back strongest.
            int strongestBin = 0;
            double strongest = 0.0;

            for (int bin = 1; bin < size / 2; bin++)
            {
                double magnitude = Math.Sqrt((real[bin] * real[bin]) + (imaginary[bin] * imaginary[bin]));

                if (magnitude > strongest)
                {
                    strongest = magnitude;
                    strongestBin = bin;
                }
            }

            // Which bin should it be? Bin B covers B x sampleRate / size hertz.
            double binWidth = (double)SampleRate / size;
            int expectedBin = (int)Math.Round(toneHz / binWidth);

            Assert.Equal(expectedBin, strongestBin);
        }

        [Theory]
        [InlineData(100.0)]
        [InlineData(440.0)]
        [InlineData(3000.0)]
        [InlineData(9000.0)]
        public void TonesAcrossTheSpectrumAllLandInTheRightPlace(double toneHz)
        {
            const int size = 1024;

            var real = new double[size];
            var imaginary = new double[size];

            float[] tone = MakeTone(toneHz, size);

            for (int i = 0; i < size; i++)
            {
                real[i] = tone[i];
            }

            FourierTransform.Forward(real, imaginary);

            int strongestBin = 0;
            double strongest = 0.0;

            for (int bin = 1; bin < size / 2; bin++)
            {
                double magnitude = Math.Sqrt((real[bin] * real[bin]) + (imaginary[bin] * imaginary[bin]));

                if (magnitude > strongest)
                {
                    strongest = magnitude;
                    strongestBin = bin;
                }
            }

            double binWidth = (double)SampleRate / size;
            double foundHz = strongestBin * binWidth;

            // Within one bin's width of the truth. Cannot be exact, because a
            // tone rarely falls precisely on a bin boundary.
            Assert.True(
                Math.Abs(foundHz - toneHz) <= binWidth,
                $"A {toneHz} Hz tone was found at {foundHz:F0} Hz.");
        }

        [Fact]
        public void SilenceContainsNoFrequencies()
        {
            const int size = 256;

            var real = new double[size];
            var imaginary = new double[size];

            FourierTransform.Forward(real, imaginary);

            for (int bin = 0; bin < size; bin++)
            {
                Assert.Equal(0.0, real[bin], precision: 10);
                Assert.Equal(0.0, imaginary[bin], precision: 10);
            }
        }

        [Theory]
        [InlineData(1000)]   // not a power of two
        [InlineData(1)]      // too short
        [InlineData(100)]
        public void LengthsThatAreNotPowersOfTwoAreRejected(int size)
        {
            // The halving trick only works on powers of two, and silently
            // producing wrong answers would be far worse than refusing.
            Assert.Throws<ArgumentException>(
                () => FourierTransform.Forward(new double[size], new double[size]));
        }

        [Fact]
        public void TheWindowFadesInAndOutSmoothly()
        {
            double[] window = FourierTransform.CreateHannWindow(128);

            // Silent at both ends, full in the middle - which is what removes
            // the sharp step that would otherwise smear energy across the whole
            // spectrum.
            Assert.Equal(0.0, window[0], precision: 6);
            Assert.Equal(0.0, window[^1], precision: 6);
            Assert.Equal(1.0, window[64], precision: 2);
        }

        // ------------------------------------------------------------------
        // Band mapping
        // ------------------------------------------------------------------

        [Fact]
        public void ThereIsOneBandPerWallColumn()
        {
            Assert.Equal(LightWall.Core.Models.WallFrame.Columns, FrequencyBands.Count);
        }

        [Fact]
        public void BandsRunFromLowToHighWithoutGaps()
        {
            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                Assert.True(FrequencyBands.GetHighEdgeHz(band) > FrequencyBands.GetLowEdgeHz(band));

                if (band > 0)
                {
                    // Each band starts exactly where the previous one ended, so
                    // no part of the spectrum is missed or counted twice.
                    Assert.Equal(
                        FrequencyBands.GetHighEdgeHz(band - 1),
                        FrequencyBands.GetLowEdgeHz(band));
                }
            }
        }

        [Fact]
        public void BandsGetWiderAsTheyGoUp()
        {
            // Hearing works in ratios, so bands are spaced logarithmically.
            // Evenly spaced bands would put nearly everything musical into the
            // first one and leave the rest almost empty.
            double previousWidth = 0.0;

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                double width = FrequencyBands.GetHighEdgeHz(band) - FrequencyBands.GetLowEdgeHz(band);

                Assert.True(width > previousWidth, $"Band {band} was not wider than the one below it.");

                previousWidth = width;
            }
        }

        [Fact]
        public void EveryBandGetsAtLeastOneBinToLookAt()
        {
            // Otherwise averaging would divide by zero, and the lowest band is
            // the one most at risk since it is the narrowest.
            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                (int first, int last) = FrequencyBands.GetBinRange(band, 1024, SampleRate);

                Assert.True(last >= first, $"Band {band} was given no bins.");
                Assert.True(first >= 1, "Bin 0 holds the signal's offset, not an audible frequency.");
                Assert.True(last < 512, "A band ran past the end of the useful output.");
            }
        }

        [Fact]
        public void BandsGetTheirBinsInAscendingOrder()
        {
            int previousLast = 0;

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                (int first, int last) = FrequencyBands.GetBinRange(band, 1024, SampleRate);

                Assert.True(first >= previousLast, $"Band {band} overlaps the one below it.");

                previousLast = last;
            }
        }

        // ------------------------------------------------------------------
        // The whole analysis chain
        // ------------------------------------------------------------------

        /// <summary>
        /// THE TEST THAT MATTERS MOST FOR HOW THE WALL LOOKS.
        ///
        /// A bass tone must light the low bands and leave the high ones alone.
        /// This is the difference between a wall that reacts to the music and a
        /// wall that reacts to everything at once.
        /// </summary>
        [Fact]
        public void ABassToneLightsTheLowBandsAndNotTheHighOnes()
        {
            var analyser = new AudioAnalyser(SampleRate);

            // 80 Hz sits inside band 1 (60-150 Hz).
            float[] tone = MakeTone(80.0, 2048, amplitude: 0.5);

            // Feed it in repeatedly, so the smoothing and automatic gain settle.
            for (int i = 0; i < 100; i++)
            {
                analyser.Process(tone, channels: 1, deltaSeconds: 0.01);
            }

            AudioFeatures features = analyser.Process(tone, channels: 1, deltaSeconds: 0.01);

            double bass = features.GetBandLevel(1);
            double treble = features.GetBandLevel(6);

            Assert.True(bass > 0.5, $"The bass band only reached {bass:F2}.");
            Assert.True(
                treble < bass * 0.5,
                $"The treble band reached {treble:F2} against bass {bass:F2}; " +
                "energy is leaking across the spectrum.");
        }

        [Fact]
        public void ATrebleToneLightsTheHighBandsAndNotTheLowOnes()
        {
            var analyser = new AudioAnalyser(SampleRate);

            // 9 kHz sits inside band 6 (6000-16000 Hz).
            float[] tone = MakeTone(9000.0, 2048, amplitude: 0.5);

            for (int i = 0; i < 100; i++)
            {
                analyser.Process(tone, channels: 1, deltaSeconds: 0.01);
            }

            AudioFeatures features = analyser.Process(tone, channels: 1, deltaSeconds: 0.01);

            double bass = features.GetBandLevel(1);
            double treble = features.GetBandLevel(6);

            Assert.True(treble > 0.5, $"The treble band only reached {treble:F2}.");
            Assert.True(
                bass < treble * 0.5,
                $"The bass band reached {bass:F2} against treble {treble:F2}.");
        }

        /// <summary>
        /// The reason each band gets its own automatic gain.
        ///
        /// Real music carries far more energy in the bass than the treble. If
        /// bands shared one reference the high columns would never move at all.
        /// Measured against itself, a quiet hi-hat is loud for a hi-hat.
        /// </summary>
        [Fact]
        public void AQuietTrebleToneStillFillsItsOwnColumn()
        {
            var analyser = new AudioAnalyser(SampleRate);

            // Twenty times quieter than the bass test above.
            float[] tone = MakeTone(9000.0, 2048, amplitude: 0.025);

            for (int i = 0; i < 200; i++)
            {
                analyser.Process(tone, channels: 1, deltaSeconds: 0.01);
            }

            AudioFeatures features = analyser.Process(tone, channels: 1, deltaSeconds: 0.01);

            Assert.True(
                features.GetBandLevel(6) > 0.5,
                $"A quiet hi-hat only reached {features.GetBandLevel(6):F2}; " +
                "the high columns would barely move on real music.");
        }

        [Fact]
        public void SilenceLetsEveryBandDecayToNothing()
        {
            var analyser = new AudioAnalyser(SampleRate);

            float[] tone = MakeTone(200.0, 2048);

            for (int i = 0; i < 50; i++)
            {
                analyser.Process(tone, channels: 1, deltaSeconds: 0.01);
            }

            // Two seconds of nothing arriving at all.
            AudioFeatures features = AudioFeatures.Silence;

            for (int i = 0; i < 200; i++)
            {
                features = analyser.ProcessSilence(0.01);
            }

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                Assert.True(
                    features.GetBandLevel(band) < 0.05,
                    $"Band {band} was still at {features.GetBandLevel(band):F2} after silence.");
            }
        }

        [Fact]
        public void StereoIsMixedDownRatherThanAnalysedSeparately()
        {
            // A bass note panned to one side is still a bass note. Analysing
            // channels separately would make the wall react to the stereo image
            // rather than to the music.
            var analyser = new AudioAnalyser(SampleRate);

            float[] mono = MakeTone(80.0, 1024, amplitude: 0.5);

            // Same tone in the left channel only, silence in the right.
            var stereo = new float[mono.Length * 2];

            for (int i = 0; i < mono.Length; i++)
            {
                stereo[i * 2] = mono[i];
                stereo[(i * 2) + 1] = 0.0f;
            }

            for (int i = 0; i < 100; i++)
            {
                analyser.Process(stereo, channels: 2, deltaSeconds: 0.01);
            }

            AudioFeatures features = analyser.Process(stereo, channels: 2, deltaSeconds: 0.01);

            Assert.True(
                features.GetBandLevel(1) > 0.5,
                "A bass note panned hard left did not register.");
        }

        [Fact]
        public void BrokenSamplesDoNotPoisonTheWholeSpectrum()
        {
            // One infinity from a misbehaving driver would otherwise make every
            // band read as nothing, with no obvious cause.
            var analyser = new AudioAnalyser(SampleRate);

            float[] tone = MakeTone(200.0, 1024, amplitude: 0.5);
            tone[100] = float.NaN;
            tone[200] = float.PositiveInfinity;

            AudioFeatures features = AudioFeatures.Silence;

            for (int i = 0; i < 50; i++)
            {
                features = analyser.Process(tone, channels: 1, deltaSeconds: 0.01);
            }

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                Assert.False(
                    double.IsNaN(features.GetBandLevel(band)),
                    $"Band {band} came back as NaN.");
            }
        }

        [Fact]
        public void AskingForABandThatDoesNotExistGivesZeroRatherThanCrashing()
        {
            // Effects index this with a column number. A wall of a different
            // width should produce a dark column, not take the app down.
            Assert.Equal(0.0, AudioFeatures.Silence.GetBandLevel(-1));
            Assert.Equal(0.0, AudioFeatures.Silence.GetBandLevel(99));
        }

        [Fact]
        public void SnapshotsAlwaysCarryTheFullSetOfBands()
        {
            // Effects rely on this, and a short array would mean silently dark
            // columns rather than an obvious failure.
            Assert.Equal(FrequencyBands.Count, AudioFeatures.Silence.BandLevels.Count);

            var tracker = new AudioLevelTracker();
            AudioFeatures features = tracker.Update(0.1, 0.2, 0.01);

            Assert.Equal(FrequencyBands.Count, features.BandLevels.Count);
        }
    }
}
