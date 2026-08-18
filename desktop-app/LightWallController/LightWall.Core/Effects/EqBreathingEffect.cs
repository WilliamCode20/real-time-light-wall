using System;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Seven bars that jump to new heights on every beat and sink back between
    /// them, like an equaliser being played rather than measured.
    ///
    /// The rise and fall is the same as Breathing - beats push the level up from
    /// wherever it is, and it falls between them. Two things are different.
    ///
    /// The bars are FILLED, from the bottom row up to their height, where the
    /// other two breathing effects draw only the moving edge. That is what makes
    /// this read as an equaliser rather than as a line: the eye sees seven solid
    /// columns of different heights, which is the shape everyone already knows.
    ///
    /// And each column picks its own height INDEPENDENTLY, where Wiggle Breathing
    /// walks from one column to the next so neighbours stay related. Independence
    /// is exactly what is wanted here - unrelated neighbours are what makes a row
    /// of bars look like an equaliser rather than a landscape.
    ///
    /// WHAT THIS IS NOT
    ///
    /// Not EQ Bumper, which is worth being clear about since the names are close.
    /// EQ Bumper is honest measurement: each column follows its own slice of the
    /// real frequency spectrum, and if the treble is silent the right-hand columns
    /// stay down.
    ///
    /// This one invents its heights. It follows the beat and nothing else, so the
    /// heights are made up rather than measured. That makes it a decorative
    /// effect rather than a diagnostic one, and it is why it must never be
    /// reached for to judge whether the audio analysis is working. EQ Bumper is
    /// the one that answers that question.
    ///
    /// A NOTE ON THE POWER BUDGET
    ///
    /// The heaviest of the three breathing effects, since the bars are filled. On
    /// an average beat something like twenty of the thirty-five bulbs are lit, and
    /// on the rare beat where every bar happens to roll high it is briefly the
    /// whole wall. That is a flash rather than a hold, which is well within what
    /// the original show did routinely - the caution in this project is about
    /// holding all thirty-five lit for minutes, not touching it for a moment.
    /// </summary>
    public sealed class EqBreathingEffect : IWallEffect
    {
        /// <summary>The rise and fall. See BreathEnvelope.</summary>
        private readonly BreathEnvelope _breath = new();

        /// <summary>
        /// How high each bar is heading this beat, in rows above the bottom one.
        /// </summary>
        private readonly int[] _targetRise = new int[WallFrame.Columns];

        /// <summary>Which beat the heights above were rolled for.</summary>
        private int _heightsForBeat = -1;

        /// <inheritdoc />
        public string DisplayName => "EQ Breathing";

        /// <inheritdoc />
        public bool ReactsToAudio => true;

        /// <inheritdoc />
        public EffectControl Controls => EffectControl.BeatSource;

        /// <inheritdoc />
        public string Description =>
            "Seven bars jump to new heights on every beat and sink back between " +
            "them. The heights are invented rather than measured - use EQ Bumper " +
            "to see the real spectrum. Start audio capture to make it listen.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            if (!context.IsAudioActive)
            {
                _breath.Forget();
                _heightsForBeat = -1;
                DrawBars(target, inflation: 0.0);
                return;
            }

            _breath.Advance(context);

            RollHeightsIfBeatIsNew(context);

            DrawBars(target, _breath.EasedInflation);
        }

        /// <summary>
        /// Rolls a fresh set of bar heights, but only when a new beat has
        /// arrived.
        ///
        /// Rolling every frame would re-randomise sixty times a second and the
        /// bars would blur into noise rather than holding a shape for the length
        /// of a beat. Tying it to the beat number also keeps the effect
        /// repeatable, since the same moment asked twice gives the same number.
        /// </summary>
        private void RollHeightsIfBeatIsNew(EffectContext context)
        {
            if (_breath.BeatNumber == _heightsForBeat)
            {
                return;
            }

            _heightsForBeat = _breath.BeatNumber;

            Random random = context.CreateRandomForStep(_breath.BeatNumber);

            for (int column = 0; column < WallFrame.Columns; column++)
            {
                // Zero is allowed, so a bar can sit the beat out on the floor
                // rather than every column always jumping together. A row of bars
                // that all move by the same kind of amount every time looks
                // mechanical; letting some stay down is most of what makes it
                // look alive.
                _targetRise[column] = random.Next(0, WallFrame.Rows);
            }
        }

        /// <summary>
        /// Lights each bar from the bottom row up to its current height.
        /// </summary>
        private void DrawBars(WallFrame target, double inflation)
        {
            for (int column = 0; column < WallFrame.Columns; column++)
            {
                // Adding a half and cutting off the decimal rather than using
                // Math.Round, which rounds a value sitting exactly halfway to the
                // nearest EVEN number. See BreathingEffect for the bug that
                // caught out.
                int rise = (int)((inflation * _targetRise[column]) + 0.5);

                // The bottom row is always lit, so a bar at rest is a floor
                // rather than a gap. Height counts upward from there.
                for (int rowOffset = 0; rowOffset <= rise; rowOffset++)
                {
                    target.SetCell(WallFrame.Rows - 1 - rowOffset, column, true);
                }
            }
        }
    }
}
