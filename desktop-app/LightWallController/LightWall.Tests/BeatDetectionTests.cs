using System;
using LightWall.Core.Audio;
using LightWall.Core.Effects;
using LightWall.Core.Models;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for beat detection and tempo estimation.
    ///
    /// The valuable ones here feed in a synthetic beat at a tempo chosen in
    /// advance and check that the same tempo comes back. That is a much stronger
    /// check than anything looking at real music, because the right answer is
    /// known rather than judged - and it needs no sound card and nothing playing.
    /// </summary>
    public class BeatDetectionTests
    {
        private const int SampleRate = 48000;

        /// <summary>
        /// Builds a short burst of noise, standing in for a drum hit.
        ///
        /// Noise rather than a tone because a real drum spreads energy across
        /// the whole spectrum, which is exactly what spectral flux is designed
        /// to notice.
        /// </summary>
        private static float[] MakeHit(int sampleCount, Random random, double amplitude = 0.7)
        {
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                // Fades away across the buffer, like a struck drum.
                double envelope = 1.0 - ((double)i / sampleCount);
                samples[i] = (float)((random.NextDouble() * 2.0 - 1.0) * amplitude * envelope);
            }

            return samples;
        }

        /// <summary>
        /// Builds a quiet background, standing in for the gaps between hits.
        /// </summary>
        private static float[] MakeQuiet(int sampleCount, Random random)
        {
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = (float)((random.NextDouble() * 2.0 - 1.0) * 0.02);
            }

            return samples;
        }

        /// <summary>
        /// Plays a steady beat at a given tempo through an analyser.
        /// </summary>
        private static AudioFeatures PlayBeat(
            AudioAnalyser analyser,
            double bpm,
            double totalSeconds,
            int seed = 4242)
        {
            var random = new Random(seed);

            const double bufferSeconds = 0.01;
            int bufferSamples = (int)(SampleRate * bufferSeconds);

            double beatInterval = 60.0 / bpm;
            double elapsed = 0.0;

            AudioFeatures features = AudioFeatures.Silence;

            while (elapsed < totalSeconds)
            {
                double intoBeat = elapsed % beatInterval;

                // A hit lasting a couple of buffers at the start of each beat.
                float[] buffer = intoBeat < 0.03
                    ? MakeHit(bufferSamples, random)
                    : MakeQuiet(bufferSamples, random);

                features = analyser.Process(buffer, channels: 1, deltaSeconds: bufferSeconds);
                elapsed += bufferSeconds;
            }

            return features;
        }

        // ------------------------------------------------------------------
        // Onset detection
        // ------------------------------------------------------------------

        [Fact]
        public void ASteadyBeatIsDetected()
        {
            var analyser = new AudioAnalyser(SampleRate);

            AudioFeatures features = PlayBeat(analyser, bpm: 120.0, totalSeconds: 10.0);

            // Ten seconds at 120 BPM is twenty beats. Allow for a few missed at
            // the start while the moving threshold settles.
            Assert.InRange(features.BeatCount, 14, 26);
        }

        [Fact]
        public void SilenceProducesNoBeats()
        {
            // The moving threshold divides by a recent average that is nearly
            // zero in silence, so without the minimum-flux guard every flicker
            // would look enormous and the wall would strobe to nothing.
            var analyser = new AudioAnalyser(SampleRate);
            var random = new Random(7);

            AudioFeatures features = AudioFeatures.Silence;

            for (int i = 0; i < 500; i++)
            {
                features = analyser.Process(MakeQuiet(480, random), 1, 0.01);
            }

            Assert.Equal(0, features.BeatCount);
        }

        [Fact]
        public void ASustainedToneIsNotMistakenForRepeatedBeats()
        {
            // The distinction the whole design rests on. A held note is loud for
            // its entire length but starts only once - detecting loudness would
            // flash continuously, detecting onsets flashes once.
            var analyser = new AudioAnalyser(SampleRate);

            var tone = new float[480];

            for (int i = 0; i < tone.Length; i++)
            {
                tone[i] = (float)(0.5 * Math.Sin(2.0 * Math.PI * 200.0 * i / SampleRate));
            }

            AudioFeatures features = AudioFeatures.Silence;

            for (int i = 0; i < 400; i++)
            {
                features = analyser.Process(tone, 1, 0.01);
            }

            // At most a couple, from the moment it began.
            Assert.True(
                features.BeatCount <= 3,
                $"A steady tone produced {features.BeatCount} beats; onsets are being " +
                "confused with loudness.");
        }

        [Fact]
        public void OneHitIsNotCountedSeveralTimes()
        {
            // A drum hit rises over several readings. Without a minimum gap
            // between beats, one kick would be reported three or four times and
            // the tempo estimate would be nonsense.
            var detector = new OnsetDetector();
            var bands = new double[FrequencyBands.Count];

            // Settle the moving threshold on a quiet background.
            for (int i = 0; i < 60; i++)
            {
                detector.Update(bands, i * 0.01);
            }

            int beats = 0;

            // A single hit spread over five readings, rising then falling.
            double[] shape = { 0.3, 0.6, 0.9, 0.7, 0.4 };

            for (int i = 0; i < shape.Length; i++)
            {
                for (int band = 0; band < bands.Length; band++)
                {
                    bands[band] = shape[i];
                }

                if (detector.Update(bands, 1.0 + (i * 0.01)))
                {
                    beats++;
                }
            }

            Assert.Equal(1, beats);
        }

        // ------------------------------------------------------------------
        // Tempo estimation
        // ------------------------------------------------------------------

        /// <summary>
        /// THE HEADLINE TEST. Beats half a second apart must read as 120 BPM.
        /// </summary>
        [Theory]
        [InlineData(90.0)]
        [InlineData(120.0)]
        [InlineData(128.0)]
        [InlineData(174.0)]
        public void PerfectlySpacedBeatsGiveTheRightTempo(double bpm)
        {
            var estimator = new TempoEstimator();

            double interval = 60.0 / bpm;

            for (int i = 0; i < 12; i++)
            {
                estimator.AddBeat(i * interval);
            }

            Assert.Equal(bpm, estimator.Bpm, precision: 0);
            Assert.Equal(1.0, estimator.Confidence, precision: 2);
        }

        [Fact]
        public void AMissedBeatDoesNotThrowTheEstimateOff()
        {
            // The reason the median is used rather than the average. One missed
            // beat produces a gap twice as long, and an average would be dragged
            // upward by it.
            var estimator = new TempoEstimator();

            double interval = 0.5;   // 120 BPM

            for (int i = 0; i < 12; i++)
            {
                // Skip the seventh beat entirely.
                if (i == 7)
                {
                    continue;
                }

                estimator.AddBeat(i * interval);
            }

            Assert.Equal(120.0, estimator.Bpm, precision: 0);
        }

        [Fact]
        public void ASpuriousExtraBeatDoesNotThrowTheEstimateOff()
        {
            var estimator = new TempoEstimator();

            double interval = 0.5;

            for (int i = 0; i < 12; i++)
            {
                estimator.AddBeat(i * interval);

                // An extra detection halfway through one gap.
                if (i == 5)
                {
                    estimator.AddBeat((i * interval) + 0.25);
                }
            }

            Assert.Equal(120.0, estimator.Bpm, precision: 0);
        }

        /// <summary>
        /// Plays a steady beat with an extra sound at a fixed point inside each
        /// one, standing in for a synth layered over the drums in a chorus.
        /// </summary>
        private static TempoEstimator PlayBeatWithExtraSound(
            double offsetIntoBeat,
            int beats = 16,
            double beatSeconds = 0.5)
        {
            var estimator = new TempoEstimator();

            for (int i = 0; i < beats; i++)
            {
                double beat = i * beatSeconds;

                estimator.AddBeat(beat);

                if (offsetIntoBeat > 0.0)
                {
                    estimator.AddBeat(beat + offsetIntoBeat);
                }
            }

            return estimator;
        }

        /// <summary>
        /// THE CHORUS TEST, and the reason the estimator was rewritten.
        ///
        /// A busy passage adds sounds between the beats. The beat underneath has
        /// not changed, so the reported tempo must not change either.
        ///
        /// Every one of these offsets except 0.25 defeated the previous version,
        /// and not by a little. Measured then: 0.28 gave 107 BPM, 0.30 gave 100,
        /// 0.35 gave 171, and 0.40 gave 150 BPM while reporting itself 100%
        /// confident. The fault was that each gap was doubled until it fell in
        /// range, which turns a slightly-off gap into a confidently wrong tempo
        /// rather than a slightly wrong one.
        /// </summary>
        [Theory]
        [InlineData(0.25)]
        [InlineData(0.28)]
        [InlineData(0.30)]
        [InlineData(0.35)]
        [InlineData(0.40)]
        public void AnExtraSoundBetweenBeatsDoesNotMoveTheTempo(double offsetIntoBeat)
        {
            TempoEstimator estimator = PlayBeatWithExtraSound(offsetIntoBeat);

            Assert.InRange(estimator.Bpm, 118.0, 122.0);
        }

        [Fact]
        public void AnOffBeatLayerCostsConfidenceRatherThanCorrectness()
        {
            // The honest answer when half the sounds are off the beat is "120,
            // and half of what I am hearing does not sit on it" - not a
            // different tempo, and not full confidence either.
            TempoEstimator clean = PlayBeatWithExtraSound(0.0);
            TempoEstimator layered = PlayBeatWithExtraSound(0.30);

            Assert.Equal(1.0, clean.Confidence, precision: 2);

            Assert.InRange(layered.Bpm, 118.0, 122.0);

            Assert.True(
                layered.Confidence < clean.Confidence,
                $"A syncopated layer reported {layered.Confidence:P0} confidence, " +
                $"against {clean.Confidence:P0} for the same beat played clean.");
        }

        [Fact]
        public void AMessyChorusDoesNotOverturnASettledTempo()
        {
            // Closer to what a real chorus does: several extra sounds per beat,
            // none of them landing in quite the same place twice.
            var estimator = new TempoEstimator();
            var random = new Random(11);

            double time = 0.0;

            // Eight seconds of clean verse to settle on.
            for (int i = 0; i < 16; i++)
            {
                estimator.AddBeat(time);
                time += 0.5;
            }

            double settled = estimator.Bpm;
            Assert.InRange(settled, 118.0, 122.0);

            // Then the chorus arrives, with the same beat still underneath.
            for (int i = 0; i < 24; i++)
            {
                estimator.AddBeat(time);
                estimator.AddBeat(time + 0.18 + (random.NextDouble() * 0.12));
                estimator.AddBeat(time + 0.33 + (random.NextDouble() * 0.12));
                time += 0.5;
            }

            Assert.InRange(estimator.Bpm, 118.0, 122.0);
        }

        [Fact]
        public void ARealTempoChangeIsPickedUpEventually()
        {
            // The other half of holding steady. Inertia must not be permanent -
            // nothing here knows where one song ends and the next begins, and at
            // a venue the next track will be at a different speed.
            var estimator = new TempoEstimator();

            double time = 0.0;

            // 120 BPM.
            for (int i = 0; i < 16; i++)
            {
                estimator.AddBeat(time);
                time += 0.5;
            }

            Assert.InRange(estimator.Bpm, 118.0, 122.0);

            // Then a new track at 150 BPM, which is 0.4 seconds a beat.
            for (int i = 0; i < 40; i++)
            {
                estimator.AddBeat(time);
                time += 0.4;
            }

            Assert.InRange(estimator.Bpm, 147.0, 153.0);
        }

        // ------------------------------------------------------------------
        // Trust: how hard a settled tempo is to shift
        // ------------------------------------------------------------------

        /// <summary>
        /// Plays a timeline of tempo sections through the estimator, ticking its
        /// clock the way AudioAnalyser does, and samples what it reported.
        ///
        /// Ticking matters. Trust moves with TIME rather than with beats, so a
        /// test that only calls AddBeat never builds any and would be measuring
        /// a mechanism that never engaged.
        ///
        /// A section with a tempo of zero means silence - no beats at all.
        /// </summary>
        private static List<(double seconds, double bpm, double trust)> PlayTempoSections(
            (double fromSeconds, double bpm)[] sections,
            double totalSeconds)
        {
            var estimator = new TempoEstimator();
            var samples = new List<(double, double, double)>();

            const double tick = 0.01;
            double nextBeatAt = 0.0;

            for (double now = 0.0; now <= totalSeconds; now += tick)
            {
                // Whichever section is in force at this moment.
                double bpm = 0.0;

                foreach ((double fromSeconds, double sectionBpm) in sections)
                {
                    if (now >= fromSeconds)
                    {
                        bpm = sectionBpm;
                    }
                }

                if (bpm > 0.0)
                {
                    if (now >= nextBeatAt)
                    {
                        estimator.AddBeat(now);
                        nextBeatAt = now + (60.0 / bpm);
                    }
                }
                else
                {
                    // Nothing playing, so the next beat is whenever sound
                    // returns rather than on the old schedule.
                    nextBeatAt = now;
                }

                estimator.Update(now);
                samples.Add((now, estimator.Bpm, estimator.Trust));
            }

            return samples;
        }

        /// <summary>
        /// When the reported tempo first settled near a given value, or -1.
        /// </summary>
        private static double FirstReached(
            List<(double seconds, double bpm, double trust)> run, double bpm)
        {
            foreach ((double seconds, double reported, double _) in run)
            {
                if (Math.Abs(reported - bpm) <= bpm * 0.03)
                {
                    return seconds;
                }
            }

            return -1.0;
        }

        [Fact]
        public void TrustIsEarnedByBeingConfirmedAndLostWhenTheMusicStops()
        {
            // Thirty seconds of steady beats, then fifteen of silence.
            var run = PlayTempoSections(
                new[] { (0.0, 120.0), (30.0, 0.0) },
                totalSeconds: 45.0);

            double afterAgreeing = run[(int)(29.0 / 0.01)].trust;
            double afterSilence = run[^1].trust;

            Assert.True(
                afterAgreeing > 0.9,
                $"Thirty seconds of agreement only earned {afterAgreeing:F2} trust.");

            Assert.True(
                afterSilence < 0.1,
                $"Fifteen seconds of silence left {afterSilence:F2} trust still standing.");
        }

        [Fact]
        public void ASettledTempoTakesLongerToShiftThanAFreshOne()
        {
            // THE POINT OF THE WHOLE MECHANISM.
            //
            // The same change of tempo, once against a tempo that has only just
            // been adopted and once against one that has held for half a minute.
            // The settled one must put up more of a fight.
            var fresh = PlayTempoSections(
                new[] { (0.0, 120.0), (6.0, 150.0) },
                totalSeconds: 40.0);

            var settled = PlayTempoSections(
                new[] { (0.0, 120.0), (35.0, 150.0) },
                totalSeconds: 70.0);

            double freshSwitchedAt = FirstReached(fresh, 150.0) - 6.0;
            double settledSwitchedAt = FirstReached(settled, 150.0) - 35.0;

            Assert.True(freshSwitchedAt > 0, "The fresh tempo never switched at all.");
            Assert.True(settledSwitchedAt > 0, "The settled tempo never switched at all.");

            Assert.True(
                settledSwitchedAt > freshSwitchedAt,
                $"A settled tempo gave way in {settledSwitchedAt:F1}s against " +
                $"{freshSwitchedAt:F1}s for a fresh one - trust is not doing anything.");
        }

        [Fact]
        public void ATrackChangeIsAlwaysAdoptedEventually()
        {
            // THE OTHER HALF, AND THE REASON TRUST DECAYS RATHER THAN ONLY
            // ACCUMULATING.
            //
            // Trust that only ever grew would be a trap: a long song would build
            // a position nothing could dislodge and the next track would never
            // get a look in. Because it erodes while the evidence is against it,
            // how long a switch takes is set by the decay rate and NOT by how
            // long the previous tempo had been running.
            //
            // Two minutes of 120 is six times as long as the thirty seconds in
            // the test above, and must not take six times as long to give way.
            var run = PlayTempoSections(
                new[] { (0.0, 120.0), (120.0, 150.0) },
                totalSeconds: 145.0);

            double switchedAfter = FirstReached(run, 150.0) - 120.0;

            Assert.True(
                switchedAfter > 0,
                "Two minutes of 120 BPM became immovable, which is the trap this " +
                "mechanism exists to avoid.");

            Assert.True(
                switchedAfter < 15.0,
                $"The new track took {switchedAfter:F1}s to be picked up, which is " +
                "long enough to be noticed as the wall fighting the music.");
        }

        [Fact]
        public void ABriefWobbleDoesNotShiftASettledTempo()
        {
            // Two seconds of a different tempo in the middle of a settled track -
            // shorter than any real section - must leave the estimate alone.
            var run = PlayTempoSections(
                new[] { (0.0, 120.0), (35.0, 150.0), (37.0, 120.0) },
                totalSeconds: 50.0);

            Assert.InRange(run[^1].bpm, 116.0, 124.0);
        }

        [Fact]
        public void SloppyTimingShowsUpAsLowerConfidence()
        {
            // The point of reporting confidence at all: a confident wrong answer
            // and an unconfident one look identical without it.
            var steady = new TempoEstimator();
            var loose = new TempoEstimator();
            var random = new Random(99);

            double time = 0.0;

            for (int i = 0; i < 12; i++)
            {
                steady.AddBeat(i * 0.5);

                time += 0.5 + ((random.NextDouble() - 0.5) * 0.25);
                loose.AddBeat(time);
            }

            Assert.True(
                loose.Confidence < steady.Confidence,
                $"Loose timing reported {loose.Confidence:F2} against steady {steady.Confidence:F2}.");
        }

        [Fact]
        public void NotEnoughBeatsMeansNoEstimateRatherThanAGuess()
        {
            // Below a handful of gaps, one bad detection would dominate and the
            // reported tempo would leap about. Admitting we do not know yet is
            // more useful than a number that changes every beat.
            var estimator = new TempoEstimator();

            estimator.AddBeat(0.0);
            estimator.AddBeat(0.5);

            Assert.Equal(0.0, estimator.Bpm);
        }

        [Fact]
        public void VerySlowBeatsAreFoldedIntoTheReportableRange()
        {
            // Tempo is genuinely ambiguous - the same music at 60 and at 120 are
            // both correct descriptions, and listeners disagree about this
            // constantly. Folding keeps the answer in a range useful for lights.
            var estimator = new TempoEstimator();

            // 50 BPM, below the reportable minimum.
            for (int i = 0; i < 12; i++)
            {
                estimator.AddBeat(i * 1.2);
            }

            Assert.InRange(estimator.Bpm, estimator.MinimumBpm, estimator.MaximumBpm);
        }

        [Fact]
        public void TheEstimateExpiresWhenTheMusicStops()
        {
            // Otherwise the last reading of a finished track sits there looking
            // like a working estimate.
            var estimator = new TempoEstimator();

            for (int i = 0; i < 12; i++)
            {
                estimator.AddBeat(i * 0.5);
            }

            Assert.True(estimator.Bpm > 0.0);

            estimator.Update(nowSeconds: 100.0);

            Assert.Equal(0.0, estimator.Bpm);
        }

        // ------------------------------------------------------------------
        // End to end
        // ------------------------------------------------------------------

        /// <summary>
        /// The whole chain: synthetic drum hits in, correct tempo out.
        /// </summary>
        [Fact]
        public void ASyntheticDrumTrackReportsItsOwnTempo()
        {
            var analyser = new AudioAnalyser(SampleRate);

            AudioFeatures features = PlayBeat(analyser, bpm: 120.0, totalSeconds: 15.0);

            Assert.True(features.TempoBpm > 0.0, "No tempo was worked out at all.");

            // Within a couple of BPM. Buffers are ten milliseconds wide, so the
            // detected moment can land slightly either side of the true beat.
            Assert.InRange(features.TempoBpm, 115.0, 125.0);
        }

        [Fact]
        public void TheTimeSinceTheLastBeatIsTracked()
        {
            var analyser = new AudioAnalyser(SampleRate);

            AudioFeatures features = PlayBeat(analyser, bpm: 120.0, totalSeconds: 8.0);

            // Somewhere within the last beat, since the music is still playing.
            Assert.InRange(features.SecondsSinceBeat, 0.0, 0.6);
        }

        [Fact]
        public void BeforeAnyBeatTheTimeSinceIsAHugeNumber()
        {
            // So that an effect asking "was there a beat recently?" gets a
            // sensible no, with nothing to check first.
            Assert.Equal(AudioFeatures.NoBeatYet, AudioFeatures.Silence.SecondsSinceBeat);
            Assert.True(AudioFeatures.Silence.SecondsSinceBeat > 100.0);
        }

        /// <summary>
        /// Plays a steady beat with a chosen amount of texture between the hits,
        /// so that tracks of very different dynamics can be compared.
        /// </summary>
        private static AudioFeatures PlayBeatWithTexture(double betweenHits)
        {
            var analyser = new AudioAnalyser(SampleRate);
            var random = new Random(4242);

            const double bufferSeconds = 0.01;
            int bufferSamples = (int)(SampleRate * bufferSeconds);

            double elapsed = 0.0;
            AudioFeatures features = AudioFeatures.Silence;

            while (elapsed < 12.0)
            {
                double intoBeat = elapsed % 0.5;

                float[] buffer = intoBeat < 0.03
                    ? MakeHit(bufferSamples, random)
                    : MakeHit(bufferSamples, random, betweenHits);

                features = analyser.Process(buffer, 1, bufferSeconds);
                elapsed += bufferSeconds;
            }

            return features;
        }

        /// <summary>
        /// THE REASON THE THRESHOLD MOVED OFF THE AVERAGE.
        ///
        /// Three tracks at the same tempo and the same peak loudness, differing
        /// only in how much is going on between the hits - near silence, moderate
        /// texture, and a dense wash. All three must read correctly at the SAME
        /// setting, because a person running a set cannot re-tune per song.
        ///
        /// This is what the old average-based threshold could not do. An average
        /// is moved by the shape of the distribution as well as its level: on
        /// sparse material the occasional huge reading drags the bar up out of
        /// reach of ordinary hits, and on dense material the average sits up
        /// among the peaks so nothing clears a multiple of it. Measured across
        /// these three tracks, no single setting read all of them right.
        /// </summary>
        [Theory]
        [InlineData(0.01)]
        [InlineData(0.10)]
        [InlineData(0.35)]
        public void OneSensitivityWorksAcrossVeryDifferentDynamics(double betweenHits)
        {
            AudioFeatures features = PlayBeatWithTexture(betweenHits);

            Assert.InRange(features.TempoBpm, 115.0, 125.0);

            Assert.True(
                features.TempoConfidence > 0.75,
                $"Texture {betweenHits} gave {features.TempoConfidence:P0} confidence at the " +
                "default setting, so this material would need the slider moved.");
        }

        // ------------------------------------------------------------------
        // The trigger meter
        //
        // These matter because the meter is what the sensitivity setting is
        // dialled in against. A meter that reads plausibly but wrongly is worse
        // than no meter at all, because it would be trusted.
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a set of band strengths all at the same level.
        ///
        /// The detector only ever adds the bands up, so spreading a hit evenly
        /// across them is as good a stand-in for a drum as anything and makes
        /// the arithmetic in these tests easy to follow.
        /// </summary>
        private static double[] MakeBands(double level)
        {
            var bands = new double[FrequencyBands.Count];

            for (int band = 0; band < bands.Length; band++)
            {
                bands[band] = level;
            }

            return bands;
        }

        [Fact]
        public void AReportedBeatAlwaysReadsAtOrAboveTheTriggerPoint()
        {
            // The promise the meter makes to the person tuning: the red line is
            // the point where a beat becomes possible. If a beat could be
            // reported while the bar sat short of the line, the meter would be
            // telling a story the detector does not agree with.
            var detector = new OnsetDetector();

            int beatsSeen = 0;

            for (int step = 0; step < 600; step++)
            {
                // A spike every thirtieth reading, quiet in between - roughly
                // three hits a second.
                double level = step % 30 == 0 ? 0.8 : 0.05;

                bool beat = detector.Update(MakeBands(level), step * 0.01);

                if (beat)
                {
                    beatsSeen++;

                    Assert.True(
                        detector.TriggerRatio >= 1.0,
                        $"A beat was reported while the meter read {detector.TriggerRatio:F2}, " +
                        "which is below the trigger point.");
                }
            }

            // Without this the test would pass on a signal that produced no
            // beats at all, which would prove nothing.
            Assert.True(beatsSeen > 0, "The test signal produced no beats to check.");
        }

        [Fact]
        public void SilenceDoesNotPinTheTriggerMeter()
        {
            // This is the case the meter is measured against the higher of the
            // two guards for. In silence the moving threshold decays to zero,
            // so a meter comparing against the threshold alone would divide by
            // nothing and read as either infinite or pinned at the top - telling
            // the person tuning that beats were on the very edge of firing when
            // in truth the room was quiet.
            var detector = new OnsetDetector();

            for (int step = 0; step < 300; step++)
            {
                detector.Update(MakeBands(0.0), step * 0.01);
            }

            Assert.True(
                double.IsFinite(detector.TriggerRatio),
                "The meter went to infinity or produced nonsense during silence.");

            Assert.True(
                detector.TriggerRatio < 1.0,
                $"Silence read {detector.TriggerRatio:F2} on the meter, which looks like a near miss.");
        }

        [Fact]
        public void TheTriggerMeterReadsNothingBeforeThereIsAnyHistory()
        {
            // On the very first reading there is nothing to compare against, so
            // the detector holds off judging. The meter should say so by sitting
            // at the bottom rather than showing a number invented from no data.
            var detector = new OnsetDetector();

            detector.Update(MakeBands(0.5), 0.0);

            Assert.Equal(0.0, detector.TriggerRatio);
        }

        // ------------------------------------------------------------------
        // The flashing effect
        // ------------------------------------------------------------------

        private static WallFrame RenderFlash(double secondsSinceBeat, bool audioActive = true)
        {
            var features = new AudioFeatures(
                0.5, 0.5, 0.5, 0.5,
                new double[FrequencyBands.Count],
                isSilent: false,
                secondsSinceBeat: secondsSinceBeat);

            var context = new EffectContext(0.0, new EffectParameters(), 1, features, audioActive);

            var frame = new WallFrame();
            new BeatFlashEffect().Render(context, frame);
            return frame;
        }

        [Fact]
        public void TheFlashLightsTheWholeWallOnABeat()
        {
            Assert.Equal(35, RenderFlash(0.0).CountLitCells());
            Assert.Equal(35, RenderFlash(0.05).CountLitCells());
        }

        [Fact]
        public void TheFlashGoesOutBetweenBeats()
        {
            Assert.Equal(0, RenderFlash(0.3).CountLitCells());
            Assert.Equal(0, RenderFlash(AudioFeatures.NoBeatYet).CountLitCells());
        }

        [Fact]
        public void TheFlashShowsOneRowWhenNothingIsListening()
        {
            WallFrame frame = RenderFlash(0.0, audioActive: false);

            Assert.Equal(WallFrame.Columns, frame.CountLitCells());
        }

        [Fact]
        public void TheFlashStaysSeparateAtFastTempos()
        {
            // At 180 BPM beats are a third of a second apart. The flash must go
            // out before the next one arrives, or it becomes a solid glow.
            double beatInterval = 60.0 / 180.0;

            Assert.Equal(0, RenderFlash(beatInterval * 0.9).CountLitCells());
        }
    }
}
