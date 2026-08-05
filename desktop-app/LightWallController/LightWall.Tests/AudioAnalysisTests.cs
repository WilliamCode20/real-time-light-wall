using System;
using LightWall.Core.Audio;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the audio maths.
    ///
    /// WHY THE MATHS WAS SEPARATED FROM THE CAPTURE
    ///
    /// Capturing real audio needs Windows, a sound device and something actually
    /// playing. A test can rely on none of those, so a test suite that needed
    /// them would fail on any machine that happened to be muted — and tests that
    /// fail for irrelevant reasons get ignored, then deleted.
    ///
    /// Keeping the arithmetic in Core means it can be checked exactly, with
    /// known inputs and answers worked out by hand. What is left untested is
    /// only the plumbing: asking Windows for buffers and handing them over.
    /// </summary>
    public class AudioAnalysisTests
    {
        // ------------------------------------------------------------------
        // Measuring a buffer
        // ------------------------------------------------------------------

        [Fact]
        public void SilenceMeasuresAsZero()
        {
            var samples = new float[512];   // all zeros

            (double rms, double peak) = AudioSampleMath.Analyse(samples);

            Assert.Equal(0.0, rms, precision: 10);
            Assert.Equal(0.0, peak, precision: 10);
        }

        [Fact]
        public void AnEmptyBufferIsTreatedAsSilenceRatherThanAnError()
        {
            (double rms, double peak) = AudioSampleMath.Analyse(Array.Empty<float>());

            Assert.Equal(0.0, rms);
            Assert.Equal(0.0, peak);
        }

        [Fact]
        public void FullScaleSquareWaveMeasuresAsOne()
        {
            // A signal alternating between the extremes. Every sample is at full
            // scale, so both RMS and peak should read 1.
            var samples = new float[100];

            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (i % 2 == 0) ? 1.0f : -1.0f;
            }

            (double rms, double peak) = AudioSampleMath.Analyse(samples);

            Assert.Equal(1.0, rms, precision: 6);
            Assert.Equal(1.0, peak, precision: 6);
        }

        [Fact]
        public void NegativeSamplesCountAsLoudAsPositiveOnes()
        {
            // A sound wave swings both above and below zero, and both halves are
            // equally loud. Averaging the raw numbers would give roughly zero
            // for any real sound, which is why RMS squares them first.
            var positive = new float[] { 0.5f, 0.5f, 0.5f, 0.5f };
            var negative = new float[] { -0.5f, -0.5f, -0.5f, -0.5f };

            (double positiveRms, double positivePeak) = AudioSampleMath.Analyse(positive);
            (double negativeRms, double negativePeak) = AudioSampleMath.Analyse(negative);

            Assert.Equal(positiveRms, negativeRms, precision: 10);
            Assert.Equal(positivePeak, negativePeak, precision: 10);
            Assert.Equal(0.5, negativeRms, precision: 6);
        }

        [Fact]
        public void SineWaveRmsIsAboutSeventyPercentOfItsPeak()
        {
            // The classic result: a sine wave's RMS is its amplitude divided by
            // the square root of two, roughly 0.707.
            //
            // This is the strongest check in the file, because it compares
            // against a value from mathematics rather than from our own code.
            const int sampleCount = 4096;
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                // Exactly 8 complete cycles, so no partial cycle skews the average.
                samples[i] = (float)Math.Sin(2.0 * Math.PI * 8.0 * i / sampleCount);
            }

            (double rms, double peak) = AudioSampleMath.Analyse(samples);

            Assert.Equal(1.0 / Math.Sqrt(2.0), rms, precision: 3);
            Assert.Equal(1.0, peak, precision: 2);
        }

        [Fact]
        public void PeakCatchesASingleTransientThatRmsSmoothsAway()
        {
            // One loud sample in an otherwise quiet buffer — a drum hit at the
            // very start of a buffer, say. Peak should see it clearly while RMS
            // barely registers it, which is exactly why both are measured.
            var samples = new float[1000];
            samples[500] = 1.0f;

            (double rms, double peak) = AudioSampleMath.Analyse(samples);

            Assert.Equal(1.0, peak, precision: 6);
            Assert.True(rms < 0.05, $"RMS was {rms}, expected it to stay low.");
        }

        [Fact]
        public void BrokenSamplesAreIgnoredRatherThanPoisoningTheResult()
        {
            // A misbehaving driver or plugin can emit these. Just one would make
            // every subsequent calculation come out as NaN, and the meter would
            // sit dead with no obvious cause.
            var samples = new float[] { 0.5f, float.NaN, 0.5f, float.PositiveInfinity, 0.5f };

            (double rms, double peak) = AudioSampleMath.Analyse(samples);

            Assert.False(double.IsNaN(rms), "A stray NaN poisoned the RMS calculation.");
            Assert.False(double.IsNaN(peak), "A stray NaN poisoned the peak calculation.");
            Assert.True(rms > 0.0);
        }

        [Fact]
        public void OutOfRangeSamplesAreClamped()
        {
            var samples = new float[] { 3.0f, -3.0f };

            (double rms, double peak) = AudioSampleMath.Analyse(samples);

            Assert.Equal(1.0, rms, precision: 6);
            Assert.Equal(1.0, peak, precision: 6);
        }

        // ------------------------------------------------------------------
        // Decibel mapping
        // ------------------------------------------------------------------

        [Fact]
        public void FullScaleMapsToOne()
        {
            Assert.Equal(1.0, AudioSampleMath.LinearToNormalisedDecibels(1.0, -60.0), precision: 6);
        }

        [Fact]
        public void SilenceMapsToZero()
        {
            Assert.Equal(0.0, AudioSampleMath.LinearToNormalisedDecibels(0.0, -60.0));
        }

        [Fact]
        public void TheHalfwayPointIsThirtyDecibelsDown()
        {
            // With a -60 dB floor, -30 dB should land at the middle of the range.
            // -30 dB is a linear amplitude of about 0.0316.
            double linear = Math.Pow(10.0, -30.0 / 20.0);

            double normalised = AudioSampleMath.LinearToNormalisedDecibels(linear, -60.0);

            Assert.Equal(0.5, normalised, precision: 4);
        }

        [Fact]
        public void OrdinaryMusicLandsInAUsefulPartOfTheRange()
        {
            // The whole reason for the decibel mapping. Music typically sits
            // around 0.05 to 0.2 RMS. Used directly that would keep a meter
            // pinned near the bottom; mapped through decibels it should occupy a
            // sensible middle stretch.
            double quiet = AudioSampleMath.LinearToNormalisedDecibels(0.05, -60.0);
            double loud = AudioSampleMath.LinearToNormalisedDecibels(0.2, -60.0);

            Assert.InRange(quiet, 0.4, 0.7);
            Assert.InRange(loud, 0.6, 0.9);

            // And louder music must still read higher than quieter music.
            Assert.True(loud > quiet);
        }

        [Fact]
        public void AnythingBelowTheFloorReadsAsZero()
        {
            // -80 dB with a -60 floor is well below the threshold.
            double linear = Math.Pow(10.0, -80.0 / 20.0);

            Assert.Equal(0.0, AudioSampleMath.LinearToNormalisedDecibels(linear, -60.0));
        }

        [Fact]
        public void APositiveFloorIsRejected()
        {
            // 0 dB is the loudest possible signal, so a floor must be negative.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => AudioSampleMath.LinearToNormalisedDecibels(0.5, 0.0));
        }

        // ------------------------------------------------------------------
        // Smoothing
        // ------------------------------------------------------------------

        [Fact]
        public void ANewTrackerStartsAtZero()
        {
            var tracker = new AudioLevelTracker();

            Assert.Equal(0.0, tracker.Level);
        }

        [Fact]
        public void TheLevelRisesQuicklyAndFallsSlowly()
        {
            // The central idea. A drum hit should snap the wall on and let it
            // decay, so rising is nearly instant while falling takes its time.
            var tracker = new AudioLevelTracker
            {
                AttackSeconds = 0.01,
                ReleaseSeconds = 0.25
            };

            // One loud reading, one buffer's worth of time.
            tracker.Update(rms: 0.5, peak: 0.5, deltaSeconds: 0.01);
            double afterRise = tracker.Level;

            // Should already be most of the way up after a single step.
            Assert.True(afterRise > 0.5, $"Level only reached {afterRise} after a loud reading.");

            // Now silence for the same length of time.
            tracker.UpdateSilent(deltaSeconds: 0.01);
            double afterFall = tracker.Level;

            // It should have barely moved, because falling is slow.
            Assert.True(
                afterFall > afterRise * 0.8,
                $"Level fell from {afterRise} to {afterFall} in one step — the release is too fast.");
        }

        [Fact]
        public void SilenceEventuallyDecaysToNothing()
        {
            var tracker = new AudioLevelTracker { ReleaseSeconds = 0.1 };

            tracker.Update(rms: 0.8, peak: 0.9, deltaSeconds: 0.01);
            Assert.True(tracker.Level > 0.5);

            // A second of silence, in realistic small steps.
            for (int i = 0; i < 100; i++)
            {
                tracker.UpdateSilent(0.01);
            }

            Assert.True(tracker.Level < 0.01, $"Level was still {tracker.Level} after a second of silence.");
        }

        [Fact]
        public void SmoothingDoesNotDependOnHowOftenItIsUpdated()
        {
            // Audio buffers do not arrive at perfectly even intervals, and the
            // buffer size varies between machines. Smoothing that assumed a
            // fixed interval would run at different speeds on different
            // computers, which would be a baffling difference to chase.
            var fewBigSteps = new AudioLevelTracker { AttackSeconds = 0.05 };
            var manySmallSteps = new AudioLevelTracker { AttackSeconds = 0.05 };

            // Same total time, different step sizes.
            for (int i = 0; i < 10; i++)
            {
                fewBigSteps.Update(0.5, 0.5, 0.02);
            }

            for (int i = 0; i < 100; i++)
            {
                manySmallSteps.Update(0.5, 0.5, 0.002);
            }

            Assert.Equal(fewBigSteps.Level, manySmallSteps.Level, precision: 2);
        }

        [Fact]
        public void AHugeTimeGapDoesNotMakeTheLevelJump()
        {
            // A pause at a debugger breakpoint, or a laptop waking from sleep,
            // can report that a very long time passed.
            var tracker = new AudioLevelTracker();

            tracker.Update(rms: 0.9, peak: 1.0, deltaSeconds: 600.0);

            Assert.InRange(tracker.Level, 0.0, 1.0);
            Assert.False(double.IsNaN(tracker.Level));
        }

        [Fact]
        public void ResetReturnsToSilence()
        {
            var tracker = new AudioLevelTracker();

            tracker.Update(0.8, 0.9, 0.01);
            Assert.True(tracker.Level > 0.0);

            tracker.Reset();

            Assert.Equal(0.0, tracker.Level);
        }

        [Fact]
        public void SnapshotsCarryTheRawReadingsAlongsideTheSmoothedOne()
        {
            var tracker = new AudioLevelTracker();

            AudioFeatures features = tracker.Update(rms: 0.3, peak: 0.7, deltaSeconds: 0.01);

            Assert.Equal(0.3, features.Rms, precision: 6);
            Assert.Equal(0.7, features.Peak, precision: 6);
            Assert.False(features.IsSilent);
        }

        [Fact]
        public void SilentSnapshotsSaySo()
        {
            // Worth knowing explicitly rather than inferring from a low level,
            // because Windows sends no buffers at all during silence — "quiet"
            // and "stopped" arrive looking quite different.
            var tracker = new AudioLevelTracker();

            AudioFeatures features = tracker.UpdateSilent(0.01);

            Assert.True(features.IsSilent);
            Assert.Equal(0.0, features.Rms);
        }

        [Fact]
        public void TheSharedSilenceSnapshotIsActuallySilent()
        {
            Assert.True(AudioFeatures.Silence.IsSilent);
            Assert.Equal(0.0, AudioFeatures.Silence.Level);
            Assert.Equal(0.0, AudioFeatures.Silence.Rms);
            Assert.Equal(0.0, AudioFeatures.Silence.Peak);
        }
    }
}
