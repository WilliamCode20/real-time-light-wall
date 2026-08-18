using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Flashes the whole wall on every detected beat.
    ///
    /// WHAT THIS IS REALLY FOR
    ///
    /// Judging beat detection by eye. Nothing else makes it so immediately
    /// obvious whether the detection is right: put music on, watch the wall, and
    /// either it lands with the drums or it does not. No numbers to interpret,
    /// no ambiguity.
    ///
    /// Deliberately the crudest possible visual. Anything more elaborate would
    /// make it harder to tell whether a flash was slightly early, slightly late,
    /// or missing entirely - and those are exactly the things worth spotting
    /// while the detection is being tuned.
    ///
    /// It is also a perfectly good effect in its own right for a strobe-like
    /// moment in a set.
    ///
    /// WHY IT FLASHES ON DETECTION RATHER THAN ON PREDICTION
    ///
    /// The tempo estimate could be used to predict when the next beat is due and
    /// flash exactly then, which would cancel out the delay through the whole
    /// chain and look perfectly in time.
    ///
    /// That is deliberately not done here. A predicted flash looks convincing
    /// whether or not the detection underneath it is working - it would hide
    /// precisely the faults this exists to reveal. Flashing on detection is
    /// honest: what you see is what was actually found.
    ///
    /// Prediction is worth having later for effects meant to look good rather
    /// than to be diagnosed.
    /// </summary>
    public sealed class BeatFlashEffect : IWallEffect
    {
        /// <summary>
        /// How long the wall stays lit after each beat, in seconds.
        ///
        /// Long enough to see clearly, short enough that beats stay separate at
        /// fast tempos. At 180 beats a minute they are a third of a second
        /// apart, so 0.1 leaves a clear gap between one flash and the next.
        ///
        /// It also keeps the wall dark most of the time, which matters here:
        /// all 35 bulbs at once sits close to the microcontroller's current
        /// limit, and a flash is a much better way to use that than holding it.
        /// </summary>
        private const double FlashSeconds = 0.1;

        /// <inheritdoc />
        public string DisplayName => "Beat Flash";

        /// <inheritdoc />
        public bool ReactsToAudio => true;

        // No beat source control on purpose, and the absence is the point. This
        // effect is pinned to beats actually heard, because a predicted flash
        // looks convincing whether or not detection worked - which is the fault
        // it exists to reveal. Offering a switch that would be ignored is worse
        // than offering none.

        /// <inheritdoc />
        public string Description =>
            "Flashes the whole wall on every detected beat. The quickest way to " +
            "see whether beat detection is working - it either lands with the " +
            "drums or it does not.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            if (!context.IsAudioActive)
            {
                // Nobody is listening. A single lit row says "running, waiting"
                // without pretending to have found a beat.
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    target.SetCell(WallFrame.Rows - 1, column, true);
                }

                return;
            }

            // A time rather than a "beat happened" flag, which is what makes
            // this work regardless of how often anyone asks. See
            // AudioFeatures.SecondsSinceBeat.
            if (context.Audio.SecondsSinceBeat < FlashSeconds)
            {
                target.Fill();
            }
        }
    }
}
