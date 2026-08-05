using System;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Bars rising from the bottom of the wall, driven by how loud the music is.
    ///
    /// NO SINE WAVE ANY MORE
    ///
    /// Earlier versions used a travelling sine wave, first as a stand-in for
    /// audio and then as decoration on top of it. Both are gone.
    ///
    /// The decorative version was actively misleading: peaks and troughs rolled
    /// across the wall that had nothing to do with the music, so it was
    /// impossible to tell at a glance whether the wall was really following the
    /// sound or just doing its own thing. An effect that invents movement makes
    /// it harder to trust the movement that is real.
    ///
    /// WHY ALL SEVEN COLUMNS MOVE TOGETHER
    ///
    /// Because there is only one number describing the music: its overall
    /// loudness. Every column is fed the same value, so every column is the same
    /// height, and the wall rises and falls as one block.
    ///
    /// That is the honest picture of what we currently measure. Making the
    /// columns differ would mean inventing the difference, which is exactly what
    /// was just removed.
    ///
    /// The real fix is frequency bands - splitting the sound so that bass drives
    /// some columns and treble others, at which point a kick drum and a hi-hat
    /// move different parts of the wall. When that arrives, only the line
    /// choosing each column's level has to change.
    /// </summary>
    public sealed class EqBumperEffect : IWallEffect
    {
        /// <inheritdoc />
        public string DisplayName => "EQ Bumper";

        /// <inheritdoc />
        public string Description =>
            "Bars driven by how loud the music is, adjusting automatically so " +
            "the system volume setting does not matter. Start audio capture to " +
            "make it listen.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            int barHeight = GetBarHeight(context);

            for (int column = 0; column < WallFrame.Columns; column++)
            {
                for (int rowOffset = 0; rowOffset < barHeight; rowOffset++)
                {
                    target.SetCell(WallFrame.Rows - 1 - rowOffset, column, true);
                }
            }
        }

        /// <summary>
        /// Works out how tall the bars should be, from 0 to 5.
        /// </summary>
        private static int GetBarHeight(EffectContext context)
        {
            if (!context.IsAudioActive)
            {
                // Nobody is listening. Show a single lit row rather than
                // nothing, so it is obvious the effect is running and waiting
                // rather than broken - and rather than inventing motion that
                // might be mistaken for a response to sound.
                return 1;
            }

            // NormalisedLevel rather than Level, deliberately.
            //
            // Level is absolute loudness, so it falls when the computer's volume
            // is turned down and the wall would bump less for no musical reason.
            // NormalisedLevel is measured against the loudest moment of the last
            // few seconds, so the volume knob cancels out and quiet music fills
            // the wall just as well as loud music.
            double level = context.Audio.NormalisedLevel;

            // No minimum here. When the music stops, the level decays to zero
            // and the wall goes properly dark, which is both what an equaliser
            // does and the honest answer to "there is no sound".
            //
            // Rounding rather than truncating, so a bar most of the way to the
            // next row shows that row instead of sitting stubbornly one short.
            //
            // AwayFromZero is specified because .NET rounds halves to the
            // nearest EVEN number by default - so Math.Round(2.5) gives 2, not
            // 3. That is the right choice for statistics, where always rounding
            // halves upward introduces a slow upward bias, and the wrong one
            // here, where it just means a bar sitting one row short at exactly
            // the halfway point for no reason anyone could guess.
            return (int)Math.Round(
                Math.Clamp(level, 0.0, 1.0) * WallFrame.Rows,
                MidpointRounding.AwayFromZero);
        }
    }
}
