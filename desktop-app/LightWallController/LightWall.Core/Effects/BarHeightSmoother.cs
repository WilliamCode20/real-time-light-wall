using System;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Converts levels into whole numbers of lit rows, without the flickering
    /// that plain rounding causes.
    ///
    /// THE PROBLEM
    ///
    /// The wall has five rows, so a level from 0 to 1 has to become one of six
    /// whole numbers. Rounding does that, and rounding chatters.
    ///
    /// A band sitting near a boundary - say a level of 0.50, which is exactly
    /// halfway between two and three rows - flips between two and three on the
    /// tiniest fluctuation. On the wall that reads as a bulb switching on and
    /// off several times a second for no visible musical reason. With seven
    /// columns all doing it independently, the whole thing looks like static.
    ///
    /// The cause is not noisy audio. It is that a boundary is infinitely sharp:
    /// a level wandering by a thousandth around 0.50 crosses it constantly.
    ///
    /// THE FIX: MAKE THE BOUNDARIES STICKY
    ///
    /// Once a bar has settled at a height, it takes more than a hair's movement
    /// to shift it. Going up needs the level to climb clearly past the boundary;
    /// coming down needs it to fall clearly below. In between, the bar simply
    /// stays where it is.
    ///
    /// This is exactly how a thermostat avoids clicking on and off around its
    /// set point, and it is called hysteresis: the answer depends slightly on
    /// which direction you arrived from.
    ///
    /// A NOTE ON STATE
    ///
    /// This remembers where each bar currently sits, which makes any effect
    /// using it depend on history rather than purely on the current moment.
    ///
    /// That is a departure from the usual rule that an effect must be a pure
    /// function of time - but audio-reactive effects are already reading a live
    /// stream, so they were never pure in that sense. What the rule was really
    /// protecting against is an effect producing different output when asked the
    /// same question twice in a row, and this does not do that: feed it the same
    /// level twice and the second answer matches the first.
    /// </summary>
    public sealed class BarHeightSmoother
    {
        /// <summary>
        /// Where each bar currently sits, in rows.
        /// </summary>
        private readonly int[] _heights;

        /// <summary>
        /// Creates a smoother for a set of bars.
        /// </summary>
        /// <param name="barCount">How many bars, usually the wall's columns.</param>
        /// <param name="maximumHeight">The tallest a bar can be, usually the wall's rows.</param>
        public BarHeightSmoother(int barCount, int maximumHeight)
        {
            if (barCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(barCount));
            }

            if (maximumHeight < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumHeight));
            }

            _heights = new int[barCount];
            MaximumHeight = maximumHeight;
        }

        /// <summary>
        /// The tallest a bar can be.
        /// </summary>
        public int MaximumHeight { get; }

        /// <summary>
        /// How far past a boundary the level must go before the bar moves,
        /// measured in rows.
        ///
        /// Zero behaves exactly like plain rounding, chatter and all. Larger
        /// values are steadier but slower to respond, and past about 0.5 the
        /// bars start visibly lagging the music.
        ///
        /// 0.2 widens the range in which a bar holds still from one row to about
        /// one and a half, which removes nearly all the chatter while staying
        /// responsive.
        /// </summary>
        public double Hysteresis { get; set; } = 0.2;

        /// <summary>
        /// Works out how many rows a bar should show, given its current level.
        /// </summary>
        /// <param name="bar">Which bar, usually the column number.</param>
        /// <param name="level">Its level, from 0 to 1.</param>
        public int GetHeight(int bar, double level)
        {
            if (bar < 0 || bar >= _heights.Length)
            {
                return 0;
            }

            double exact = Math.Clamp(level, 0.0, 1.0) * MaximumHeight;
            int current = _heights[bar];

            // Where plain rounding would put it, with halves going upward.
            // .NET rounds halves to the nearest EVEN number by default, which
            // would leave a bar one row short at exactly the halfway point.
            int rounded = (int)Math.Round(exact, MidpointRounding.AwayFromZero);
            rounded = Math.Clamp(rounded, 0, MaximumHeight);

            // The band in which the bar holds still. Normally a boundary sits
            // half a row either side; the margin pushes both further out.
            double upperBoundary = current + 0.5 + Hysteresis;
            double lowerBoundary = current - 0.5 - Hysteresis;

            if (exact >= upperBoundary || exact < lowerBoundary)
            {
                current = rounded;
                _heights[bar] = current;
            }

            // Anything in between leaves the bar exactly where it was, which is
            // the whole point.
            return current;
        }

        /// <summary>
        /// Drops every bar back to nothing.
        /// </summary>
        public void Reset()
        {
            Array.Clear(_heights);
        }
    }
}
