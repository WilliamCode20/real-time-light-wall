using System;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// A bright head with a fading tail behind it, travelling across one row at
    /// a time and dropping down a row after each pass.
    ///
    /// This is a "procedural" effect: nothing is stored in advance, the picture
    /// is worked out from arithmetic every time it is asked for.
    ///
    /// The drawing logic used to live in a separate WallProceduralAnimations
    /// class, with only half of "the meteor" here and the other half over there.
    /// It now lives in one place, which is easier to follow and easier to change.
    /// </summary>
    public sealed class MeteorEffect : IWallEffect
    {
        /// <summary>
        /// How many cells the meteor advances per second at 100% speed.
        ///
        /// This replaces what used to be a 120-millisecond timer interval.
        /// One step every 120 ms works out to roughly 8.3 steps per second, so
        /// 8.0 keeps the effect looking essentially the same as before.
        /// </summary>
        private const double StepsPerSecond = 8.0;

        /// <inheritdoc />
        public string DisplayName => "Meteor";

        /// <inheritdoc />
        /// <remarks>The tail length slider belongs to this effect alone.</remarks>
        public EffectControl Controls => EffectControl.MeteorTail;

        /// <inheritdoc />
        public string Description =>
            "A lit head with a trailing tail sweeps across each row in turn. " +
            "Tail length is adjustable.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            // Start from a blank wall. Without this, every cell the meteor has
            // ever touched would stay lit and the wall would just fill up.
            target.Clear();

            int step = context.GetStep(StepsPerSecond);

            // A tail of zero or less would be invisible, and a negative one
            // would make the loop below misbehave, so insist on at least 1.
            int tailLength = Math.Max(1, context.Parameters.MeteorTailLength);

            // One full pass across a row takes 7 steps to cross the wall, plus
            // however many extra steps the tail needs to finish exiting on the
            // right-hand side. Without those extra steps the tail would be
            // chopped off mid-pass as the meteor jumped to the next row.
            int stepsPerPass = WallFrame.Columns + tailLength;

            // Which row this pass is on. After the bottom row it wraps back to
            // the top and starts over.
            int row = (step / stepsPerPass) % WallFrame.Rows;

            // How far across that row the bright head currently is.
            int headColumn = step % stepsPerPass;

            // Draw the head, then each tail cell behind it.
            //
            // Cells whose position falls outside the wall are skipped rather
            // than drawn, which is what produces the effect of the meteor
            // sliding in from the left edge and off the right edge.
            for (int tailOffset = 0; tailOffset < tailLength; tailOffset++)
            {
                int column = headColumn - tailOffset;

                if (column >= 0 && column < WallFrame.Columns)
                {
                    target.SetCell(row, column, true);
                }
            }
        }
    }
}
