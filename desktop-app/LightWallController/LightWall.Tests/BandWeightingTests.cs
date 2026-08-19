using System;
using LightWall.Core.Audio;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for weighting the frequency bands by how well each one agrees with
    /// the beat.
    ///
    /// The properties worth pinning here are mostly about what it must NOT do.
    /// The idea is sound and the measurements behind it are strong, but two of
    /// the obvious ways to build it are traps, and both are checked below: it
    /// must not silence a band outright, and it must not punish the band
    /// carrying the beat when the tempo has settled at half speed.
    /// </summary>
    public class BandWeightingTests
    {
        private const double ReadingInterval = 0.051;

        /// <summary>
        /// Builds a flux reading with one band carrying everything.
        /// </summary>
        private static double[] FluxIn(int band, double amount)
        {
            var flux = new double[FrequencyBands.Count];
            flux[band] = amount;
            return flux;
        }

        /// <summary>
        /// Plays a stretch of beats, letting the caller decide what each band
        /// does at each point in the beat.
        /// </summary>
        private static BandBeatAgreement Play(
            double seconds,
            Func<double, double[]> fluxAtPhase,
            double beatSeconds = 0.5)
        {
            var agreement = new BandBeatAgreement();
            double now = 0.0;

            while (now < seconds)
            {
                double phase = (now % beatSeconds) / beatSeconds;
                agreement.Observe(fluxAtPhase(phase), phase, ReadingInterval);
                now += ReadingInterval;
            }

            return agreement;
        }

        [Fact]
        public void ABandThatAlwaysLandsOnTheBeatAgreesStrongly()
        {
            // Band 0 fires only at the very start of each beat; band 4 dribbles
            // constantly and so lands everywhere in the beat equally.
            BandBeatAgreement agreement = Play(20.0, phase =>
            {
                var flux = new double[FrequencyBands.Count];
                if (phase < 0.12) flux[0] = 0.5;
                flux[4] = 0.05;
                return flux;
            });

            Assert.True(
                agreement.GetAgreement(0) > 0.8,
                $"A band landing on every beat scored only {agreement.GetAgreement(0):F2}.");

            Assert.True(
                agreement.GetAgreement(4) < 0.3,
                $"A band spread evenly through the beat scored {agreement.GetAgreement(4):F2}, " +
                "which is far too high for something with no rhythm at all.");

            Assert.True(
                agreement.GetWeight(0) > agreement.GetWeight(4),
                "The band carrying the beat should be worth more than the one that is not.");
        }

        /// <summary>
        /// THE TRAP THAT MADE THE HALF-BEAT TERM NECESSARY.
        ///
        /// An under-fed detector characteristically settles at half the true
        /// tempo. If agreement were measured only once per beat, a kick landing
        /// on every REAL beat would arrive at opposite sides of that
        /// half-speed circle alternately and cancel to nothing - so the band
        /// carrying the beat perfectly would be weighted down to its floor, and
        /// the weighting would drive itself deeper into the error.
        /// </summary>
        [Fact]
        public void ABandOnEveryBeatStillAgreesWhenTheTempoHasSettledAtHalfSpeed()
        {
            // The metronome believes a beat is 1.0 s. The music is putting a hit
            // every 0.5 s - every real beat, twice per believed beat.
            var agreement = new BandBeatAgreement();
            double now = 0.0;

            while (now < 20.0)
            {
                double believedPhase = (now % 1.0) / 1.0;
                bool onRealBeat = (now % 0.5) < 0.12;

                agreement.Observe(
                    onRealBeat ? FluxIn(0, 0.5) : new double[FrequencyBands.Count],
                    believedPhase,
                    ReadingInterval);

                now += ReadingInterval;
            }

            Assert.True(
                agreement.GetAgreement(0) > 0.8,
                $"The band carrying every beat scored {agreement.GetAgreement(0):F2} " +
                "against a half-speed metronome. Measuring only once per beat would " +
                "cancel it to nothing and weight down the one band worth hearing.");
        }

        [Fact]
        public void NoBandIsEverSilenced()
        {
            // Band 0 does all the work; the rest do something arrhythmic.
            BandBeatAgreement agreement = Play(20.0, phase =>
            {
                var flux = new double[FrequencyBands.Count];
                if (phase < 0.12) flux[0] = 0.9;
                for (int b = 1; b < FrequencyBands.Count; b++) flux[b] = 0.02;
                return flux;
            });

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                Assert.True(
                    agreement.GetWeight(band) > 0.0,
                    $"Band {band} was weighted to nothing. A band that cannot be heard " +
                    "can never show it has started carrying the beat, so the weighting " +
                    "could never hand back to it.");
            }
        }

        [Fact]
        public void TheWeightsAverageOneSoTheFluxKeepsItsScale()
        {
            BandBeatAgreement agreement = Play(20.0, phase =>
            {
                var flux = new double[FrequencyBands.Count];
                if (phase < 0.12) flux[0] = 0.9;
                for (int b = 1; b < FrequencyBands.Count; b++) flux[b] = 0.05;
                return flux;
            });

            double total = 0.0;
            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                total += agreement.GetWeight(band);
            }

            // Otherwise the flux would shrink whenever the weighting became
            // uneven, and the threshold - which is built from recent flux -
            // would spend its time chasing the weighting rather than the music.
            Assert.Equal(FrequencyBands.Count, total, precision: 6);
        }

        [Fact]
        public void EveryBandIsEqualUntilThereIsSomethingToGoOn()
        {
            var fresh = new BandBeatAgreement();

            Assert.False(fresh.IsReady);

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                Assert.Equal(1.0, fresh.GetWeight(band), precision: 9);
            }
        }

        [Fact]
        public void ForgettingReturnsToWeightingEverythingEqually()
        {
            BandBeatAgreement agreement = Play(20.0, phase =>
                phase < 0.12 ? FluxIn(0, 0.9) : new double[FrequencyBands.Count]);

            Assert.True(agreement.GetWeight(0) > agreement.GetWeight(3));

            agreement.Forget();

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                Assert.Equal(1.0, agreement.GetWeight(band), precision: 9);
            }
        }

        /// <summary>
        /// The detector must behave exactly as it did before any of this until a
        /// tempo exists and is worth believing.
        ///
        /// Measured without that gate, weighting made things clearly worse -
        /// mean time to settle across eleven real recordings went from 15.6 s to
        /// 18.8 s - because agreement measured against a wrong beat promotes
        /// whichever bands happen to fit the wrong beat.
        /// </summary>
        [Fact]
        public void NoWeightingHappensUntilTheTempoIsWorthBelieving()
        {
            static double[] Bands(double level)
            {
                var bands = new double[FrequencyBands.Count];
                for (int b = 0; b < bands.Length; b++) bands[b] = level;
                return bands;
            }

            var detector = new OnsetDetector { UseBandWeighting = true };
            double now = 0.0;

            // A tempo is known, but almost nothing is landing on it.
            while (now < 15.0)
            {
                detector.TempoHintBpm = 120.0;
                detector.TempoConfidenceHint = 0.2;
                detector.BeatPhaseHint = (now % 0.5) / 0.5;

                detector.Update(Bands((now % 0.5) < 0.1 ? 0.5 : 0.02), now);
                now += ReadingInterval;
            }

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                Assert.Equal(1.0, detector.BandAgreement.GetWeight(band), precision: 9);
            }
        }
    }
}
