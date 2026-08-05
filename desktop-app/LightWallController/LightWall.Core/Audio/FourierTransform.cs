using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Works out which frequencies a piece of sound contains.
    ///
    /// WHAT THIS DOES, IN PLAIN TERMS
    ///
    /// A buffer of audio samples describes how the speaker cone moves over time.
    /// That tells you how loud the sound is, but nothing about what it is made
    /// of - a kick drum and a cymbal at the same volume look equally large.
    ///
    /// A Fourier transform re-describes the same sound as a set of frequencies:
    /// "there is a lot of energy around 60 Hz and a little around 8000 Hz". That
    /// is what lets a bass drum move one part of the wall and a hi-hat another.
    ///
    /// WHY THIS IS WRITTEN OUT HERE RATHER THAN TAKEN FROM A LIBRARY
    ///
    /// NAudio includes one, and using it would have been less code. But it lives
    /// on the far side of LightWall.IO, and pulling it into Core would mean Core
    /// depending on a Windows audio library to do arithmetic - breaking the rule
    /// that Core knows nothing about platforms.
    ///
    /// Keeping it here means the whole analysis chain can be tested with no
    /// audio hardware, no Windows and no sound playing, against signals whose
    /// answers are known in advance. That is worth sixty lines.
    ///
    /// WHY IT IS CALLED "FAST"
    ///
    /// Done directly, working out the strength of every frequency means
    /// comparing the signal against every frequency in turn - about a million
    /// operations for a 1024-sample buffer, a hundred times a second.
    ///
    /// The fast version notices that the work contains the same sub-problems
    /// over and over, splits the buffer in half repeatedly, and reuses them.
    /// That drops it to about ten thousand operations - a hundredfold saving,
    /// which is the difference between practical and impossible.
    /// </summary>
    public static class FourierTransform
    {
        /// <summary>
        /// Transforms a signal in place, replacing it with its frequency
        /// content.
        ///
        /// The two arrays together hold complex numbers - real and imaginary
        /// parts. Sound is real-valued, so on the way in the imaginary array is
        /// all zeros; on the way out both carry meaning, and it is their
        /// combined size that says how strong each frequency is.
        ///
        /// The length must be a power of two (256, 512, 1024...). That is what
        /// makes the halving trick work.
        /// </summary>
        public static void Forward(double[] real, double[] imaginary)
        {
            if (real is null)
            {
                throw new ArgumentNullException(nameof(real));
            }

            if (imaginary is null)
            {
                throw new ArgumentNullException(nameof(imaginary));
            }

            if (real.Length != imaginary.Length)
            {
                throw new ArgumentException("The two halves must be the same length.");
            }

            int length = real.Length;

            if (length < 2 || (length & (length - 1)) != 0)
            {
                throw new ArgumentException(
                    $"Length must be a power of two, but was {length}.",
                    nameof(real));
            }

            ReorderForHalving(real, imaginary);
            CombineHalves(real, imaginary);
        }

        /// <summary>
        /// Shuffles the samples into the order the halving needs.
        ///
        /// The algorithm repeatedly splits the signal into even-numbered and
        /// odd-numbered samples. Doing that physically at each step would mean
        /// copying data over and over.
        ///
        /// Instead the whole shuffle is done once, up front. The trick is that
        /// the final position of each sample turns out to be its index with the
        /// bits written backwards - sample 1 (0b001) ends up at position 4
        /// (0b100) in an 8-sample buffer. Reversing the bits of every index
        /// therefore produces exactly the order needed, in one pass.
        /// </summary>
        private static void ReorderForHalving(double[] real, double[] imaginary)
        {
            int length = real.Length;

            for (int i = 1, j = 0; i < length; i++)
            {
                // Count upward in reversed-bit order alongside the normal count.
                int bit = length >> 1;

                for (; (j & bit) != 0; bit >>= 1)
                {
                    j ^= bit;
                }

                j ^= bit;

                // Swap only once per pair, hence the comparison.
                if (i < j)
                {
                    (real[i], real[j]) = (real[j], real[i]);
                    (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
                }
            }
        }

        /// <summary>
        /// Builds the answer back up, combining ever-larger halves.
        ///
        /// Starts by combining neighbouring pairs, then pairs of pairs, and so
        /// on until the whole buffer has been merged. Each merge step is the
        /// same operation at a different size, which is why the loop nests the
        /// way it does.
        ///
        /// The multiplications by "spin" are what encode the frequency being
        /// tested. Each step rotates a little further around a circle - the
        /// mathematical heart of the transform, and the reason sine and cosine
        /// appear at all.
        /// </summary>
        private static void CombineHalves(double[] real, double[] imaginary)
        {
            int length = real.Length;

            for (int blockSize = 2; blockSize <= length; blockSize <<= 1)
            {
                // How far to rotate between neighbouring entries in this block.
                double angle = -2.0 * Math.PI / blockSize;
                double stepReal = Math.Cos(angle);
                double stepImaginary = Math.Sin(angle);

                int half = blockSize / 2;

                for (int blockStart = 0; blockStart < length; blockStart += blockSize)
                {
                    // Start each block pointing straight along the real axis.
                    double spinReal = 1.0;
                    double spinImaginary = 0.0;

                    for (int offset = 0; offset < half; offset++)
                    {
                        int top = blockStart + offset;
                        int bottom = top + half;

                        // Rotate the lower half entry, then add and subtract it
                        // from the upper one. This single pairing is what the
                        // whole algorithm is built from.
                        double rotatedReal =
                            (real[bottom] * spinReal) - (imaginary[bottom] * spinImaginary);

                        double rotatedImaginary =
                            (real[bottom] * spinImaginary) + (imaginary[bottom] * spinReal);

                        real[bottom] = real[top] - rotatedReal;
                        imaginary[bottom] = imaginary[top] - rotatedImaginary;

                        real[top] += rotatedReal;
                        imaginary[top] += rotatedImaginary;

                        // Advance the rotation for the next entry.
                        double nextSpinReal =
                            (spinReal * stepReal) - (spinImaginary * stepImaginary);

                        spinImaginary =
                            (spinReal * stepImaginary) + (spinImaginary * stepReal);

                        spinReal = nextSpinReal;
                    }
                }
            }
        }

        /// <summary>
        /// Fills an array with a Hann window - a smooth bell shape used to taper
        /// the ends of a buffer before transforming it.
        ///
        /// WHY TAPERING IS NEEDED
        ///
        /// The transform assumes the buffer repeats forever. A real buffer
        /// almost never ends where it started, so that imagined repetition has a
        /// sharp step in it - and a sharp step contains every frequency at once.
        ///
        /// The result is energy smeared across the whole spectrum, so a pure
        /// bass note appears to have treble in it. That is called spectral
        /// leakage, and on a light wall it would show up as the treble columns
        /// twitching along with the kick drum.
        ///
        /// Fading each buffer in and out removes the step, at the cost of
        /// discarding a little information at the edges. Well worth it.
        /// </summary>
        public static double[] CreateHannWindow(int length)
        {
            if (length < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            var window = new double[length];

            for (int i = 0; i < length; i++)
            {
                // Rises from 0 to 1 and back to 0 across the buffer.
                window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (length - 1)));
            }

            return window;
        }
    }
}
