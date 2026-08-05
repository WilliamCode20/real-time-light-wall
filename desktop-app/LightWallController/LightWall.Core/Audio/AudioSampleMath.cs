using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// The arithmetic for turning raw sound samples into numbers we can use.
    ///
    /// WHY THIS LIVES IN CORE RATHER THAN BESIDE THE AUDIO CAPTURE
    ///
    /// So it can be tested. Capturing real audio needs Windows, a sound card and
    /// something actually playing - none of which a test can rely on. But the
    /// maths is just numbers in and numbers out, so pulling it out here means it
    /// can be checked exactly, with known inputs and hand-worked answers.
    ///
    /// What is left in LightWall.IO is only the plumbing: ask Windows for
    /// buffers of sound and hand them to this.
    /// </summary>
    public static class AudioSampleMath
    {
        /// <summary>
        /// Measures the loudness of a buffer of samples.
        ///
        /// Samples arrive as numbers between -1 and +1, where 0 is silence and
        /// the extremes are as loud as the format allows. A sound wave swings
        /// above and below zero constantly, so both halves count as loud.
        ///
        /// Returns two numbers:
        ///
        ///   Rms  - root mean square, which tracks perceived loudness. Square
        ///          every sample (making negatives positive and emphasising
        ///          large values), average them, take the square root.
        ///
        ///   Peak - the largest single distance from zero, which catches sharp
        ///          transients that RMS smooths away.
        ///
        /// An empty buffer gives zero for both, which is the sensible reading
        /// for "no sound at all" rather than an error.
        /// </summary>
        public static (double Rms, double Peak) Analyse(ReadOnlySpan<float> samples)
        {
            if (samples.Length == 0)
            {
                return (0.0, 0.0);
            }

            double sumOfSquares = 0.0;
            double peak = 0.0;

            foreach (float sample in samples)
            {
                // Guard against a stray infinity or "not a number" in the
                // stream. These can appear from a misbehaving audio driver or
                // plugin, and just one would poison the whole average - every
                // subsequent calculation would come out as NaN and the meter
                // would sit dead with no obvious cause.
                if (float.IsNaN(sample) || float.IsInfinity(sample))
                {
                    continue;
                }

                double value = sample;

                sumOfSquares += value * value;

                double magnitude = Math.Abs(value);

                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }

            double rms = Math.Sqrt(sumOfSquares / samples.Length);

            // Audio should never exceed 1, but a badly behaved source can send
            // values slightly outside it. Clamping keeps everything downstream
            // working in a predictable 0-to-1 range.
            return (Math.Clamp(rms, 0.0, 1.0), Math.Clamp(peak, 0.0, 1.0));
        }

        /// <summary>
        /// Converts a raw 0-to-1 loudness into a 0-to-1 value that matches how
        /// loudness actually feels.
        ///
        /// WHY THIS IS NEEDED - the most important idea in this file
        ///
        /// Hearing is logarithmic. Doubling the physical energy of a sound does
        /// not sound twice as loud; it sounds slightly louder. Decibels exist
        /// to describe that.
        ///
        /// The practical consequence for us: ordinary music sits at an RMS of
        /// roughly 0.05 to 0.2. A meter driven by that number directly would
        /// spend its whole life in the bottom fifth, twitching, and anything
        /// visual driven by it would barely move.
        ///
        /// Converting to decibels and then rescaling spreads real music across
        /// the whole range, which is what makes a level meter look right and
        /// what will make audio-reactive lighting look intentional rather than
        /// broken.
        ///
        /// The floor - how quiet counts as "nothing" - is a judgement call.
        /// Around -60 dB works well for music, where everything below is either
        /// silence or room noise.
        /// </summary>
        /// <param name="linear">Raw loudness from 0 to 1.</param>
        /// <param name="minimumDecibels">
        /// The quietest level that should register at all, as a negative number.
        /// Anything at or below this comes out as 0.
        /// </param>
        public static double LinearToNormalisedDecibels(double linear, double minimumDecibels)
        {
            if (minimumDecibels >= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumDecibels),
                    "The floor must be negative, since 0 dB is the loudest possible signal.");
            }

            // Exactly zero has no logarithm - it is infinitely quiet - so it has
            // to be handled before doing any maths.
            if (linear <= 0.0)
            {
                return 0.0;
            }

            // The standard conversion. 20 rather than 10 because these are
            // amplitude measurements rather than power measurements.
            double decibels = 20.0 * Math.Log10(linear);

            // Rescale so the floor becomes 0 and full scale becomes 1.
            // At -60 dB with a -60 floor: (-60 + 60) / 60 = 0
            // At -30 dB with a -60 floor: (-30 + 60) / 60 = 0.5
            // At   0 dB with a -60 floor: (0   + 60) / 60 = 1
            double normalised = (decibels - minimumDecibels) / -minimumDecibels;

            return Math.Clamp(normalised, 0.0, 1.0);
        }
    }
}
