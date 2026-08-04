using System;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Each column behaves like a bar on a graphic equaliser, rising from the
    /// bottom of the wall and falling back down in a travelling wave.
    ///
    /// IMPORTANT: THIS IS NOT LISTENING TO ANYTHING YET
    ///
    /// The bar heights come from a sine wave, not from audio. No music is being
    /// analysed anywhere in the app at this point.
    ///
    /// Its real value is as a target to aim at. The shape of this effect - one
    /// height value per column, redrawn continuously - is exactly the shape real
    /// audio will produce later. When frequency analysis arrives, the sine wave
    /// gets swapped for actual measured energy per frequency band and everything
    /// around it stays as it is.
    /// </summary>
    public sealed class EqBumperEffect : IWallEffect
    {
        /// <summary>
        /// How many times per second the bars update at 100% speed.
        /// Replaces the old 100-millisecond timer interval.
        /// </summary>
        private const double StepsPerSecond = 10.0;

        /// <summary>
        /// How far the wave advances each step. Larger values make the bars
        /// pump faster.
        /// </summary>
        private const double WaveSpeed = 0.45;

        /// <summary>
        /// How much the wave is offset from one column to the next.
        ///
        /// This is what makes the motion read as a wave travelling sideways
        /// across the wall. Set it to zero and all seven bars would rise and
        /// fall in unison, which looks far more mechanical.
        /// </summary>
        private const double ColumnPhaseOffset = 0.75;

        /// <summary>
        /// How much random wobble to add to each bar, as a fraction of full
        /// height. A small amount keeps the motion from looking too perfect.
        /// </summary>
        private const double JitterAmount = 0.25;

        /// <inheritdoc />
        public string DisplayName => "EQ Bumper";

        /// <inheritdoc />
        public string Description =>
            "Equaliser-style bars rise and fall in a travelling wave. " +
            "Currently driven by a sine wave rather than by real audio.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            int step = context.GetStep(StepsPerSecond);
            Random random = context.CreateRandomForStep(step);

            for (int column = 0; column < WallFrame.Columns; column++)
            {
                // Give each column its own place in the wave so the bars move
                // in a sweeping ripple rather than all together.
                double phase = (step * WaveSpeed) + (column * ColumnPhaseOffset);

                // Math.Sin swings between -1 and +1. Adding 1 and halving
                // reshapes that into a 0-to-1 range, which is easier to turn
                // into a bar height.
                double normalizedHeight = (Math.Sin(phase) + 1.0) / 2.0;

                // Add a little wobble, centred on zero so it is equally likely
                // to nudge a bar up or down rather than biasing it one way.
                double jitter = (random.NextDouble() * JitterAmount) - (JitterAmount / 2.0);

                // Clamp back into 0-to-1 in case the wobble pushed us outside it.
                double finalHeight = Math.Clamp(normalizedHeight + jitter, 0.0, 1.0);

                // Convert the 0-to-1 value into a whole number of lit cells.
                // Every bar shows at least one cell, so the bottom row stays lit
                // and the wall keeps a visible "floor" for the bars to sit on.
                int barHeight = 1 + (int)Math.Round(finalHeight * (WallFrame.Rows - 1));

                // Fill upward from the bottom row.
                for (int rowOffset = 0; rowOffset < barHeight; rowOffset++)
                {
                    target.SetCell(WallFrame.Rows - 1 - rowOffset, column, true);
                }
            }
        }
    }
}
