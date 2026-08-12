using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// A chequered pattern that swaps over on every beat.
    ///
    /// There are exactly two ways to chequer a wall - each bulb is either lit in
    /// one of them or lit in the other, never both and never neither. This shows
    /// one of them, and every beat swaps to the other.
    ///
    /// The wall is therefore never dark. Roughly half the bulbs are lit at all
    /// times, and a beat does not turn anything off so much as hand the lighting
    /// over to the other half. That is what gives it its snap: every single bulb
    /// on the wall changes at once, so the beat is impossible to miss even from
    /// across a room.
    ///
    /// WHY THIS NEEDS NO MEMORY
    ///
    /// Worth pointing out because the breathing effects next door all keep state,
    /// and this one is the opposite extreme.
    ///
    /// It never has to remember which board it is showing, because the beat
    /// number already says: even beats get one, odd beats get the other. Counting
    /// beats is something the audio side is doing anyway, so the switching comes
    /// out of arithmetic rather than out of anything held here.
    ///
    /// That makes it a pure function of the moment it is asked about, which is
    /// what nearly every effect in this project is supposed to be. It cannot get
    /// out of step, there is nothing to reset, and drawing the same moment twice
    /// gives the same picture without any care being taken.
    ///
    /// HOW THE CHEQUERING WORKS
    ///
    /// Add a bulb's row and column together. If the answer is even it belongs to
    /// one board, and if it is odd it belongs to the other - which is exactly the
    /// alternating pattern of a chessboard, since stepping one place in any
    /// direction always flips even to odd.
    ///
    /// Adding the beat number to that sum is what swaps the boards over. An even
    /// beat number leaves every bulb where it was; an odd one flips every answer,
    /// and so flips the whole wall.
    ///
    /// A NOTE ON THE POWER BUDGET
    ///
    /// Different in kind from the flashing effects, and worth stating plainly.
    /// This holds around eighteen of the thirty-five bulbs lit CONTINUOUSLY
    /// rather than touching a high number for an instant. That is roughly a
    /// hundred milliamps against the two hundred the board can supply, so it is
    /// comfortable - but it is a sustained load rather than a flash, which is the
    /// distinction the caution in this project is really about.
    /// </summary>
    public sealed class CheckerboardSwitchEffect : IWallEffect
    {
        /// <inheritdoc />
        public string DisplayName => "Checkerboard Switch";

        /// <inheritdoc />
        public string Description =>
            "A chequered pattern that swaps to its opposite on every beat. Half " +
            "the wall is always lit, and every bulb changes at once. Start audio " +
            "capture to make it listen.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            // Which of the two boards to show.
            //
            // With nothing listening there is nothing to swap to, so it settles on
            // the first board and holds still. That is a deliberate difference
            // from the other audio effects, which show a single lit row while
            // waiting: this effect is defined by never being dark, and dropping to
            // one row would break that for the sake of a convention it does not
            // need. Holding still is signal enough that nothing is being heard.
            //
            // Which count this is - beats actually heard, or the tempo metronome -
            // is the user's choice. See BeatSource.
            int whichBoard = context.IsAudioActive ? context.BeatCount : 0;

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    // Even sum means this bulb belongs to the board currently
                    // showing. See the note on chequering above.
                    bool belongsToThisBoard = (row + column + whichBoard) % 2 == 0;

                    if (belongsToThisBoard)
                    {
                        target.SetCell(row, column, true);
                    }
                }
            }
        }
    }
}
