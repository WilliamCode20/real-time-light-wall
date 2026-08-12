using System;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Where an effect should take its beat from.
    ///
    /// The project has two answers to "when is the next beat", and they are
    /// genuinely different things rather than two implementations of one thing.
    /// Which one suits depends on the music and on taste, so it is a choice the
    /// person running the wall gets to make rather than one baked into each
    /// effect.
    ///
    /// This lives here, alongside the other front-panel settings, because it is
    /// not specific to any one effect. Every effect that does something on the
    /// beat faces the same choice, and they should all answer it the same way at
    /// any given moment - it would be strange for two effects on screen to
    /// disagree about when the beat was.
    /// </summary>
    public enum BeatSource
    {
        /// <summary>
        /// Beats actually heard in the music.
        ///
        /// Honest: when the drums stop, so does the wall. Right for percussive
        /// material, and the only sensible choice while beat detection is being
        /// judged, since it shows what was really found.
        ///
        /// The cost is that it inherits every miss and every false alarm, and it
        /// falls silent through a breakdown.
        /// </summary>
        Detected,

        /// <summary>
        /// A metronome running at the detected tempo.
        ///
        /// Keeps perfect time and carries straight through a passage with
        /// nothing to detect, so a breakdown still pulses.
        ///
        /// The cost is that it is a prediction. If the tempo estimate is wrong it
        /// will be confidently, evenly wrong - which looks more convincing than
        /// it deserves. It also does nothing at all until a tempo has been worked
        /// out, since a metronome with no tempo has nothing to count.
        /// </summary>
        Tempo
    }

    /// <summary>
    /// Holds the user-adjustable settings that effects can read while drawing.
    ///
    /// Think of this as "the knobs on the front panel".
    ///
    /// The user moves a slider in the window, the window writes the new value
    /// into this object, and the next time an effect draws a frame it reads the
    /// updated value. That means sliders take effect immediately, mid-animation,
    /// without restarting anything.
    ///
    /// Note on what is NOT in here:
    ///
    /// Speed and the Center X/Y offsets are deliberately absent. Those are not
    /// handled by individual effects - they are handled by WallEngine, because
    /// they apply the same way to every effect. An effect should only need to
    /// worry about settings that are genuinely its own business.
    ///
    /// Note on future growth:
    ///
    /// Right now there is exactly one effect-specific setting, so a single
    /// shared object is the simplest thing that works. Once there are many
    /// effects each with their own settings, this will want to become a
    /// per-effect parameter system instead. That change is easy to make later
    /// and there is no benefit to building it before it is needed.
    /// </summary>
    public sealed class EffectParameters
    {
        /// <summary>
        /// How many cells long the meteor's glowing trail should be.
        ///
        /// Used only by MeteorEffect. A value of 1 means "just the head, no tail".
        /// </summary>
        public int MeteorTailLength { get; set; } = 3;

        /// <summary>
        /// Which single bulb the Identify Bulb effect should light, from 0 to 34.
        ///
        /// Used only during hardware checking, to confirm that the relay labels
        /// and the pin map match the physical wall.
        /// </summary>
        public int IdentifyBulbIndex { get; set; }

        /// <summary>
        /// Whether beat-driven effects should follow beats actually heard, or a
        /// metronome running at the detected tempo. See BeatSource.
        ///
        /// Unlike the two settings above, this is not the business of any single
        /// effect - it is a question every beat-driven effect has to answer, and
        /// they should all answer it the same way. Read it through
        /// EffectContext.BeatCount rather than directly, so no effect has to
        /// implement the choice itself.
        ///
        /// Two effects deliberately IGNORE this. Beat Flash always follows what
        /// was heard and Tempo Pulse always follows the metronome, because their
        /// job is to show the difference between the two rather than to look
        /// good. Letting either be switched would remove the only honest
        /// reference point for judging whether detection is working.
        ///
        /// Defaults to what is actually heard, which is the answer that cannot
        /// invent a beat that was not there.
        /// </summary>
        public BeatSource BeatSource { get; set; } = BeatSource.Detected;

        /// <summary>
        /// Creates a copy of these parameters.
        ///
        /// This is useful when something needs a stable snapshot of the settings
        /// that will not change underneath it - for example a test, or (later) a
        /// background thread that renders frames while the user is still moving
        /// sliders on the main thread.
        /// </summary>
        public EffectParameters Clone()
        {
            return new EffectParameters
            {
                MeteorTailLength = MeteorTailLength,
                IdentifyBulbIndex = IdentifyBulbIndex,
                BeatSource = BeatSource
            };
        }
    }
}
