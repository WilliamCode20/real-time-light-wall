using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Lights exactly one bulb and holds it there, for checking the wiring.
    ///
    /// WHAT THIS IS FOR
    ///
    /// This is the one question no amount of work at a desk can answer: does
    /// relay A1 actually switch the bulb in the top-left corner?
    ///
    /// Everything else about the wall has been settled indoors. The protocol is
    /// tested, the framing recovers from damage, the rate limiting is measured,
    /// and the virtual wall proves the whole chain agrees with itself. What none
    /// of it can prove is whether the wiring matches what everybody believes,
    /// because the app and the firmware will agree perfectly with each other
    /// while both being wrong about the physical object.
    ///
    /// So: light bulb 0, walk round the front, see which bulb is on, write it
    /// down. Repeat 35 times. Half an hour of dull work that converts the last
    /// remaining assumption into a fact.
    ///
    /// WHY IT DOES NOT ADVANCE BY ITSELF
    ///
    /// Deliberately manual. An automatic sweep would be moving on while you were
    /// still walking round the wall to look at it, and you would spend the whole
    /// session chasing it. Stepping happens when you press the button.
    ///
    /// WHAT IT SHOWS
    ///
    /// The window displays the bulb number, its row and column, the relay label
    /// on the sticker, and the Arduino pin - all at once. That way a reading can
    /// be checked against the physical wall AND against the sticker on the relay
    /// that just clicked, which catches a wiring error and a labelling error as
    /// two distinguishable problems rather than one confusing one.
    /// </summary>
    public sealed class BulbIdentifyEffect : IWallEffect
    {
        /// <inheritdoc />
        public string DisplayName => "Identify Bulb";

        /// <inheritdoc />
        public string Description =>
            "Lights one bulb at a time so the wiring can be checked against the " +
            "relay labels. Use the Previous and Next buttons to step through.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            int bulbIndex = context.Parameters.IdentifyBulbIndex;

            // Ignore a nonsensical value rather than throwing. This is a
            // diagnostic tool used while somebody is up a ladder - leaving the
            // wall dark is a far better failure than taking the app down.
            if (bulbIndex < 0 || bulbIndex >= WallHardwareMap.BulbCount)
            {
                return;
            }

            (int row, int column) = WallHardwareMap.GetPosition(bulbIndex);

            target.SetCell(row, column, true);
        }
    }
}
