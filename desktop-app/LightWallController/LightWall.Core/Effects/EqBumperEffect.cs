using System;
using LightWall.Core.Audio;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// A real graphic equaliser: each column follows its own slice of the
    /// frequency spectrum.
    ///
    /// The left of the wall follows the low end - the thump of a kick drum - and
    /// the right follows the high end, where cymbals and hi-hats live. Between
    /// them sit the bass, the body of most instruments, and the range vocals cut
    /// through.
    ///
    /// That mapping is why the wall is seven columns wide and the sound is split
    /// into seven bands. Band N drives column N.
    ///
    /// WHAT THIS REPLACED
    ///
    /// Two earlier versions, both worth remembering as mistakes.
    ///
    /// The first used a travelling sine wave in place of audio. The second kept
    /// the sine wave as decoration on top of real audio, which was worse: peaks
    /// rolled across the wall that had nothing to do with the music, so it was
    /// impossible to tell whether the wall was following the sound or inventing
    /// movement. There is no invented movement here at all - everything the wall
    /// does is measured.
    ///
    /// The third followed overall loudness, which was honest but made all seven
    /// columns identical. The wall throbbed as one block. Splitting the spectrum
    /// is what finally makes the columns mean something different from each
    /// other.
    ///
    /// WHY THE HIGH COLUMNS WORK AT ALL
    ///
    /// Worth knowing, because the obvious implementation fails here. Bass
    /// carries vastly more energy than treble in most music - often a hundred
    /// times more. Compared against each other, the bass columns would sit at
    /// full height and the treble columns would never move.
    ///
    /// Each band is instead measured against its own recent history, so a quiet
    /// hi-hat counts as loud *for a hi-hat*. See SpectrumAnalyser.
    /// </summary>
    public sealed class EqBumperEffect : IWallEffect
    {
        /// <summary>
        /// Stops the bars chattering between two heights when a level sits near
        /// a row boundary.
        ///
        /// Without this the wall looks like static: with only five rows, a band
        /// hovering around the halfway point of a row flips back and forth
        /// several times a second, and seven columns all doing it independently
        /// reads as noise rather than as music.
        ///
        /// See BarHeightSmoother, including a note on why holding state here is
        /// acceptable for an audio-reactive effect.
        /// </summary>
        private readonly BarHeightSmoother _bars =
            new(WallFrame.Columns, WallFrame.Rows);

        /// <inheritdoc />
        public string DisplayName => "EQ Bumper";

        /// <inheritdoc />
        public bool ReactsToAudio => true;

        /// <inheritdoc />
        public string Description =>
            "A graphic equaliser: bass on the left, treble on the right, each " +
            "column following its own part of the sound. Start audio capture to " +
            "make it listen.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            if (!context.IsAudioActive)
            {
                // Nobody is listening. Show a single lit row rather than
                // nothing, so it is obvious the effect is running and waiting
                // rather than broken - and without inventing motion that might
                // be mistaken for a response to sound.
                _bars.Reset();

                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    target.SetCell(WallFrame.Rows - 1, column, true);
                }

                return;
            }

            for (int column = 0; column < WallFrame.Columns; column++)
            {
                // Column 0 gets the lowest band, so the wall reads left to right
                // the way an equaliser display does.
                //
                // GetBandLevel is forgiving about columns beyond the number of
                // bands: a mismatch produces a dark column rather than a crash.
                double level = context.Audio.GetBandLevel(column);

                // Through the smoother rather than rounded directly, so a level
                // sitting near a row boundary does not make the top bulb chatter.
                int barHeight = _bars.GetHeight(column, level);

                for (int rowOffset = 0; rowOffset < barHeight; rowOffset++)
                {
                    target.SetCell(WallFrame.Rows - 1 - rowOffset, column, true);
                }
            }
        }

    }
}
