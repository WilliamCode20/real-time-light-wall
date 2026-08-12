using System;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Breathing, but the line never settles into the same shape twice.
    ///
    /// Everything about the rise and fall is the same as Breathing - beats push
    /// the line up from wherever it is, and it sinks back between them. The only
    /// difference is where it is heading. Instead of climbing towards a tidy
    /// arch, each breath picks its own wandering profile: higher on the left
    /// perhaps, dipping through the middle, rising again towards the right, with
    /// a couple of humps that were not there last time.
    ///
    /// HOW THE SHAPE IS PICKED
    ///
    /// A random walk across the columns. The leftmost column starts somewhere
    /// low, and each column after it steps up or down a little from its
    /// neighbour.
    ///
    /// A walk rather than an independent roll per column, and the difference
    /// matters. Rolling each column on its own gives a row of unrelated spikes -
    /// noise, which reads as broken rather than as a shape. Stepping from the
    /// previous column keeps neighbours related, so the result wanders like a
    /// line drawn by hand. That is what makes it a wiggle rather than a mess.
    ///
    /// WHY THE SHAPE IS TIED TO THE BEAT NUMBER
    ///
    /// The profile is worked out from the beat that started the breath rather
    /// than drawn fresh each frame. Drawing fresh would re-roll it sixty times a
    /// second and the line would dissolve into static instead of holding a shape
    /// for the length of a breath.
    ///
    /// It also keeps the effect repeatable: the same moment asked twice gives the
    /// same beat number, so it gives the same picture. Starburst places its
    /// bursts the same way and for the same reason.
    ///
    /// One consequence worth knowing: a beat arriving while the line is still
    /// high swaps the target profile immediately, so the line reshuffles where it
    /// stands rather than easing across. That is deliberate. It lands on the beat,
    /// which is exactly where a change should land, and under quick beats it gives
    /// the shimmer that makes this different from plain Breathing.
    ///
    /// A NOTE ON THE POWER BUDGET
    ///
    /// The same as Breathing. Being a surface, it is exactly seven bulbs at every
    /// instant whatever the music does.
    /// </summary>
    public sealed class WiggleBreathingEffect : IWallEffect
    {
        /// <summary>
        /// How far the leftmost column can start above the floor.
        ///
        /// Not the full height, so there is somewhere to wander upward to. If it
        /// could start at the top the walk would only ever be able to come down.
        /// </summary>
        private const int HighestStart = 3;

        /// <summary>
        /// The steps the walk may take from one column to the next.
        ///
        /// Weighted by simply repeating the common ones: mostly a single row
        /// either way, occasionally two, and sometimes level. Written out as a
        /// list rather than worked out from a distribution because at this size
        /// the list IS the description - what the line can do is visible at a
        /// glance, and it can be tuned by eye by editing it.
        /// </summary>
        private static readonly int[] AllowedSteps = { -2, -1, -1, 0, 0, 1, 1, 2 };

        /// <summary>The rise and fall. See BreathEnvelope.</summary>
        private readonly BreathEnvelope _breath = new();

        /// <summary>
        /// How high each column is heading this breath, in rows above the floor.
        ///
        /// Held rather than rebuilt every frame, since it only changes when the
        /// breath does. Rebuilt whenever the beat number moves on.
        /// </summary>
        private readonly int[] _targetRise = new int[WallFrame.Columns];

        /// <summary>Which beat the profile above was worked out for.</summary>
        private int _profileForBeat = -1;

        /// <inheritdoc />
        public string DisplayName => "Wiggle Breathing";

        /// <inheritdoc />
        public string Description =>
            "Breathing with a mind of its own: the line rises on each beat but " +
            "settles into a different wandering shape every time. Start audio " +
            "capture to make it listen.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            if (!context.IsAudioActive)
            {
                _breath.Forget();
                _profileForBeat = -1;
                DrawFlatLine(target);
                return;
            }

            _breath.Advance(context);

            PickProfileIfBreathIsNew(context);

            double inflation = _breath.EasedInflation;

            for (int column = 0; column < WallFrame.Columns; column++)
            {
                // Adding a half and cutting off the decimal rather than using
                // Math.Round, which rounds a value sitting exactly halfway to the
                // nearest EVEN number. See BreathingEffect for the bug that
                // caught out.
                int rise = (int)((inflation * _targetRise[column]) + 0.5);

                target.SetCell(WallFrame.Rows - 1 - rise, column, true);
            }
        }

        /// <summary>
        /// Works out a fresh wandering profile, but only when a new breath has
        /// started.
        /// </summary>
        private void PickProfileIfBreathIsNew(EffectContext context)
        {
            if (_breath.BeatNumber == _profileForBeat)
            {
                return;
            }

            _profileForBeat = _breath.BeatNumber;

            // Tied to the beat number, so the same breath always wanders the same
            // way however many times it is drawn.
            Random random = context.CreateRandomForStep(_breath.BeatNumber);

            int tallest = WallFrame.Rows - 1;

            _targetRise[0] = random.Next(0, HighestStart + 1);

            for (int column = 1; column < WallFrame.Columns; column++)
            {
                int step = AllowedSteps[random.Next(AllowedSteps.Length)];

                _targetRise[column] = Math.Clamp(
                    _targetRise[column - 1] + step,
                    0,
                    tallest);
            }
        }

        /// <summary>
        /// The resting shape: a flat line along the bottom row, same as
        /// Breathing.
        /// </summary>
        private static void DrawFlatLine(WallFrame target)
        {
            for (int column = 0; column < WallFrame.Columns; column++)
            {
                target.SetCell(WallFrame.Rows - 1, column, true);
            }
        }
    }
}
