using System;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Scattered cells flicker on and off at random, with the number of lit
    /// cells gently swelling and shrinking, plus a repeating accent on the
    /// centre bulb to give the chaos a sense of pulse.
    ///
    /// A NOTE ON RANDOMNESS AND REDRAWING
    ///
    /// This effect gets its random numbers from context.CreateRandomForStep
    /// rather than from a single shared generator.
    ///
    /// That is what stops it flickering uncontrollably. The simulator redraws
    /// roughly 60 times a second while this effect is only meant to change 9
    /// times a second, so the same step gets drawn several times in a row.
    /// Tying the randomness to the step number means those repeat draws produce
    /// an identical picture, and the sparkles hold steady between changes.
    ///
    /// With a shared generator they would all differ, and the wall would be a
    /// blur rather than a sparkle.
    /// </summary>
    public sealed class SparkleStormEffect : IWallEffect
    {
        /// <summary>
        /// How many times per second the sparkle arrangement changes at 100%
        /// speed. Replaces the old 110-millisecond timer interval, which was
        /// about 9 changes per second.
        /// </summary>
        private const double StepsPerSecond = 9.0;

        /// <summary>
        /// The fewest sparkles visible at once.
        /// </summary>
        private const int MinimumSparkles = 4;

        /// <summary>
        /// How much the sparkle count swells above the minimum before it resets.
        /// With a minimum of 4 and a range of 5, the count cycles 4,5,6,7,8.
        /// </summary>
        private const int SparkleCountRange = 5;

        /// <summary>
        /// How often the centre accent fires, measured in steps.
        /// Every 4th step gives a steady on-beat feel against the random cells.
        /// </summary>
        private const int CentreAccentInterval = 4;

        /// <inheritdoc />
        public string DisplayName => "Sparkle Storm";

        /// <inheritdoc />
        public string Description =>
            "Random cells flicker across the wall with a swelling and fading " +
            "density, punctuated by a repeating centre accent.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            int step = context.GetStep(StepsPerSecond);
            Random random = context.CreateRandomForStep(step);

            // Let the density breathe over time so the storm feels alive rather
            // than like a constant, even fizz.
            int sparkleCount = MinimumSparkles + (Math.Abs(step) % SparkleCountRange);

            for (int i = 0; i < sparkleCount; i++)
            {
                int row = random.Next(0, WallFrame.Rows);
                int column = random.Next(0, WallFrame.Columns);

                target.SetCell(row, column, true);
            }

            // Because the picks above are independent, two can land on the same
            // cell. That means slightly fewer cells light up than the count
            // suggests. That is fine here - it adds to the uneven, natural feel.

            // A regular accent on the centre bulb. This gives the eye something
            // predictable to hold onto, which is what stops the effect reading
            // as meaningless noise.
            if (Math.Abs(step) % CentreAccentInterval == 0)
            {
                target.SetCell(WallFrame.Rows / 2, WallFrame.Columns / 2, true);
            }
        }
    }
}
