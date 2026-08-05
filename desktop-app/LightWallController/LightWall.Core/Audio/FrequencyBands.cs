using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Defines how the audible spectrum is carved into the bands that drive the
    /// wall's columns.
    ///
    /// WHY SEVEN, AND WHY THESE BOUNDARIES
    ///
    /// Seven because the wall is seven columns wide, so each column gets its own
    /// slice of the sound. That is the whole point: a kick drum should move the
    /// left of the wall and a hi-hat the right.
    ///
    /// The boundaries are spaced roughly logarithmically rather than evenly, and
    /// that is not arbitrary. Hearing works in ratios, not differences: the gap
    /// between 100 and 200 Hz sounds like the same musical distance as the gap
    /// between 1000 and 2000 Hz, even though one is ten times wider in Hz.
    ///
    /// Spacing the bands evenly - 0-2000, 2000-4000 and so on - would put almost
    /// everything musically interesting into the first band and leave the rest
    /// nearly empty. Nearly all the energy in music lives below 1000 Hz.
    ///
    /// The chosen ranges follow how the parts of a mix actually sit:
    ///
    ///   20 - 60 Hz      the thump of a kick drum, felt more than heard
    ///   60 - 150 Hz     bass guitar and the body of the kick
    ///   150 - 400 Hz    low end of vocals, guitars, snare body
    ///   400 - 1000 Hz   the core of most instruments
    ///   1000 - 2500 Hz  where vocals cut through
    ///   2500 - 6000 Hz  presence and attack; consonants, pick noise
    ///   6000 - 16000 Hz air and sparkle; cymbals, hi-hats
    /// </summary>
    public static class FrequencyBands
    {
        /// <summary>
        /// How many bands the sound is split into.
        ///
        /// Deliberately equal to the wall's column count, so band N drives
        /// column N. If the wall were ever a different width, this would follow
        /// it.
        /// </summary>
        public const int Count = 7;

        /// <summary>
        /// The boundaries between bands, in hertz.
        ///
        /// There are eight numbers for seven bands, because each band needs a
        /// bottom and a top and neighbours share.
        ///
        /// The top stops at 16 kHz rather than the theoretical 20 kHz limit of
        /// hearing, because there is almost nothing up there in real music and
        /// including it would leave the last column permanently dark.
        /// </summary>
        private static readonly double[] EdgesHz =
        {
            20.0,
            60.0,
            150.0,
            400.0,
            1000.0,
            2500.0,
            6000.0,
            16000.0
        };

        /// <summary>
        /// A short human-readable name for each band, for use in the interface.
        /// </summary>
        private static readonly string[] Names =
        {
            "Sub",
            "Bass",
            "Low mid",
            "Mid",
            "Upper mid",
            "Presence",
            "Air"
        };

        /// <summary>
        /// The lowest frequency included in a band, in hertz.
        /// </summary>
        public static double GetLowEdgeHz(int band)
        {
            ValidateBand(band);
            return EdgesHz[band];
        }

        /// <summary>
        /// The highest frequency included in a band, in hertz.
        /// </summary>
        public static double GetHighEdgeHz(int band)
        {
            ValidateBand(band);
            return EdgesHz[band + 1];
        }

        /// <summary>
        /// A short name such as "Bass" or "Air".
        /// </summary>
        public static string GetName(int band)
        {
            ValidateBand(band);
            return Names[band];
        }

        /// <summary>
        /// Works out which range of transform outputs belongs to a band.
        ///
        /// A Fourier transform of N samples produces N/2 useful outputs, called
        /// bins, evenly spaced from 0 Hz up to half the sample rate. Bin B
        /// therefore covers the frequency:
        ///
        ///     B x sampleRate / windowSize
        ///
        /// Rearranged, that gives the bin for a frequency, which is what this
        /// does for each end of the band.
        ///
        /// The result is clamped so that a band cannot run off the end of the
        /// available bins, and every band is guaranteed at least one bin - so a
        /// low band at a low sample rate produces a real reading rather than an
        /// empty average.
        /// </summary>
        /// <param name="band">Which band, from 0 to Count - 1.</param>
        /// <param name="windowSize">How many samples the transform was given.</param>
        /// <param name="sampleRate">Samples per second of the audio.</param>
        public static (int FirstBin, int LastBin) GetBinRange(
            int band,
            int windowSize,
            int sampleRate)
        {
            ValidateBand(band);

            if (windowSize < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(windowSize));
            }

            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            // Only the first half of the transform's output is meaningful; the
            // rest mirrors it, which is a consequence of the input being a real
            // signal rather than a complex one.
            int highestBin = (windowSize / 2) - 1;

            double binsPerHertz = (double)windowSize / sampleRate;

            int firstBin = (int)Math.Floor(GetLowEdgeHz(band) * binsPerHertz);
            int lastBin = (int)Math.Ceiling(GetHighEdgeHz(band) * binsPerHertz) - 1;

            // Bin 0 holds the constant offset of the signal rather than any
            // audible frequency, so it is never useful here.
            firstBin = Math.Clamp(firstBin, 1, highestBin);
            lastBin = Math.Clamp(lastBin, 1, highestBin);

            // Guarantee at least one bin, so averaging never divides by zero.
            if (lastBin < firstBin)
            {
                lastBin = firstBin;
            }

            return (firstBin, lastBin);
        }

        /// <summary>
        /// Makes sure a band number is one that exists.
        /// </summary>
        private static void ValidateBand(int band)
        {
            if (band < 0 || band >= Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(band),
                    $"Band must be between 0 and {Count - 1}.");
            }
        }
    }
}
