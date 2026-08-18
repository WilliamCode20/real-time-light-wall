using System;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// The wall breathes: a line lifts off the bottom row, curves up into a
    /// rounded arch and sinks back again, like a chest rising and falling.
    ///
    /// At rest it is a flat line along the bottom row. Each beat draws the breath
    /// in - the middle of the line climbing fastest and the ends lagging, so the
    /// line bows upward - and between beats it lets back out.
    ///
    /// The rise and fall itself lives in BreathEnvelope, shared with the other
    /// two breathing effects. Worth reading if the timing matters: beats push the
    /// level up from wherever it is rather than restarting it, which is what
    /// stops quick beats slamming the line into the floor over and over.
    ///
    /// It is a SURFACE, not a filled shape: exactly one bulb per column, with
    /// nothing lit underneath. See DrawSurface for why that matters more than it
    /// sounds.
    ///
    /// A NOTE ON THE POWER BUDGET
    ///
    /// The gentlest effect in the catalogue. Being a surface, it is exactly seven
    /// bulbs at every instant no matter what the music does - a fifth of the wall
    /// and nowhere near the current limit that holding all thirty-five would
    /// approach.
    /// </summary>
    public sealed class BreathingEffect : IWallEffect
    {
        /// <summary>
        /// How far past the edge of the wall the arch is shaped as though it
        /// continued.
        ///
        /// This is what stops the outer columns being pinned to the floor. A
        /// half-circle drawn exactly the width of the wall reaches zero at the
        /// last column, so the ends of the line would never lift at all. Shaping
        /// it as though the circle carried on a little past both edges means the
        /// wall shows the middle portion of a wider arch, and the ends come up
        /// with everything else.
        /// </summary>
        private const double ShoulderAllowance = 0.6;

        /// <summary>The rise and fall. See BreathEnvelope.</summary>
        private readonly BreathEnvelope _breath = new();

        /// <inheritdoc />
        public string DisplayName => "Breathing";

        /// <inheritdoc />
        public bool ReactsToAudio => true;

        /// <inheritdoc />
        public EffectControl Controls => EffectControl.BeatSource;

        /// <inheritdoc />
        public string Description =>
            "A line lifts off the bottom row into a rounded arch and sinks back, " +
            "like a chest rising and falling. Beats push it up rather than " +
            "restarting it. Start audio capture to make it listen.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            // With nothing listening there is no breath to be part way through,
            // so the line rests flat. That happens to be the same picture the
            // other audio effects show while waiting, which is convenient rather
            // than confusing: this effect's resting state genuinely IS the bottom
            // row, and it is not pretending to have heard anything.
            if (!context.IsAudioActive)
            {
                _breath.Forget();
                DrawSurface(target, 0.0);
                return;
            }

            _breath.Advance(context);

            DrawSurface(target, _breath.EasedInflation);
        }

        /// <summary>
        /// Lights the surface of the breath at a given fullness.
        ///
        /// EXACTLY ONE BULB PER COLUMN, AND WHY
        ///
        /// Only the top of each column is lit - everything underneath stays dark.
        /// What travels up and down the wall is a single surface, the way the top
        /// of a chest moves while the chest itself does not light up.
        ///
        /// The first version filled each column from the bottom row upwards, so
        /// the arch was a solid mass. It read as a block growing rather than as
        /// something breathing: the eye follows the moving edge, and filling in
        /// behind it buries that edge in a wall of light instead of leaving it as
        /// the thing being watched.
        ///
        /// A useful side effect is that the whole effect is seven bulbs at any
        /// moment, whatever it is doing.
        /// </summary>
        private static void DrawSurface(WallFrame target, double inflation)
        {
            for (int column = 0; column < WallFrame.Columns; column++)
            {
                // Adding a half and cutting off the decimal, rather than using
                // Math.Round. .NET rounds a value sitting exactly halfway to the
                // NEAREST EVEN number, so Math.Round(2.5) gives 2 rather than 3.
                // That has already caused one bug in this project, where bars sat
                // a row short of where they should have been at the halfway point.
                int rise = (int)((inflation * FullRiseForColumn(column)) + 0.5);

                // Row 0 is the top of the wall, so rising means counting
                // backwards from the bottom row.
                target.SetCell(WallFrame.Rows - 1 - rise, column, true);
            }
        }

        /// <summary>
        /// How high a column reaches at the top of a breath, in rows.
        ///
        /// THE SHAPE OF THE ARCH
        ///
        /// A circle, flattened to fit. Height falls away from the middle the way
        /// it does around the top of a circle: barely at all near the centre,
        /// then faster towards the edges. On a wall seven wide and five tall the
        /// fullest breath comes out like this:
        ///
        ///     ..###..
        ///     .#...#.
        ///     #.....#
        ///     .......
        ///     .......
        ///
        /// and at rest, the flat line it grew out of:
        ///
        ///     .......
        ///     .......
        ///     .......
        ///     .......
        ///     #######
        ///
        /// WHAT THIS REPLACED
        ///
        /// A straight taper - each column simply one row lower than its
        /// neighbour nearer the middle. It was easy to reason about and it looked
        /// like a pyramid: a sharp point in the middle, dead straight sides, and
        /// the outer columns barely lifting off the floor. Nothing about it
        /// suggested anything being inflated.
        ///
        /// The rounded version differs in exactly the two ways that matter. The
        /// middle three columns now reach the same height, so the top is a short
        /// flat span rather than a point. And the outer columns rise two rows
        /// instead of one, so the ends of the line lift clear of the floor
        /// instead of staying pinned to it.
        /// </summary>
        private static double FullRiseForColumn(int column)
        {
            double middle = (WallFrame.Columns - 1) / 2.0;
            double fromMiddle = Math.Abs(column - middle);

            // Shaping the circle as though it were slightly wider than the wall
            // is what lifts the outer columns. See ShoulderAllowance.
            double reach = middle + ShoulderAllowance;
            double acrossTheArch = fromMiddle / reach;

            // The height of a circle at a given distance from its centre. Flat
            // near the middle and dropping away increasingly steeply, which is
            // precisely the roundness a straight taper cannot produce.
            double heightOfCircle = Math.Sqrt(1.0 - (acrossTheArch * acrossTheArch));

            // The bottom row is where the line rests, so the middle column has
            // the remaining rows to climb through.
            return (WallFrame.Rows - 1) * heightOfCircle;
        }
    }
}
