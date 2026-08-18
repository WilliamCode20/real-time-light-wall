using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Pulses the whole wall steadily at the estimated tempo, whether or not
    /// anything is currently being played.
    ///
    /// HOW THIS DIFFERS FROM BEAT FLASH
    ///
    /// Beat Flash fires on beats actually heard. It is honest, and right for
    /// reacting to drum hits - but real music does not oblige with a hit on
    /// every beat. Drums drop out, a bar goes past on nothing but a pad, and the
    /// wall goes quiet along with them.
    ///
    /// This runs on a metronome instead. Once the tempo is known it keeps
    /// counting regardless, so a breakdown still pulses in time and the wall
    /// carries the beat through the gap.
    ///
    /// Detected beats still matter - they keep the metronome aligned - but they
    /// no longer decide when it fires.
    ///
    /// THE TRADE
    ///
    /// This can be wrong in a way Beat Flash cannot, because it is a prediction
    /// rather than an observation. If the tempo estimate is off, this drifts
    /// steadily out of time and looks confident while doing it.
    ///
    /// So both exist, and which to use depends on the music. Beat Flash for
    /// something percussive and irregular; this for anything with a steady pulse
    /// that occasionally goes quiet.
    /// </summary>
    public sealed class TempoPulseEffect : IWallEffect
    {
        /// <inheritdoc />
        public string DisplayName => "Tempo Pulse";

        /// <inheritdoc />
        public bool ReactsToAudio => true;

        // No beat source control either, for the mirror image of the reason Beat
        // Flash has none: this one is pinned to the metronome, so that between
        // them the pair show the difference between heard and predicted.

        /// <inheritdoc />
        public string Description =>
            "Pulses steadily at the estimated tempo, carrying straight through " +
            "quiet passages where nothing is being played. Beat Flash reacts to " +
            "hits; this keeps time.";

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            target.Clear();

            // Nothing listening, or the tempo is not yet known. A single lit row
            // says "running, waiting" without pretending to have found a beat.
            if (!context.IsAudioActive || context.Audio.TempoBpm <= 0.0)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    target.SetCell(WallFrame.Rows - 1, column, true);
                }

                return;
            }

            // How long each pulse lasts, as a fraction of the gap between beats.
            //
            // A fraction rather than a fixed number of seconds, so the pulse
            // keeps the same feel at any tempo - a fifth of a beat looks the
            // same whether the music is fast or slow, where a fixed 100
            // milliseconds would be a brief blink at 90 BPM and nearly solid at
            // 180.
            if (context.Audio.BeatPhase < PulseFraction)
            {
                target.Fill();
            }
        }

        /// <summary>
        /// How much of each beat the wall stays lit for.
        ///
        /// Also keeps the wall dark most of the time, which matters here: all 35
        /// bulbs at once sits close to the microcontroller's current limit, and
        /// pulsing is a far better use of that than holding.
        /// </summary>
        private const double PulseFraction = 0.22;
    }
}
