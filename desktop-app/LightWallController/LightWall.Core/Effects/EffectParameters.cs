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
    /// How much of a stepped animation one beat is worth.
    ///
    /// The same sequence of pictures can be paced two quite different ways, and
    /// which is wanted is a matter of taste rather than of correctness - so it is
    /// offered as a choice rather than settled in the code.
    /// </summary>
    public enum FillPacing
    {
        /// <summary>
        /// One picture per beat.
        ///
        /// Slow and deliberate. A whole fill-and-clear cycle takes as many beats
        /// as it has pictures - six for the horizontal version, eight for the
        /// vertical - so at 120 beats a minute it is a three or four second
        /// cycle. The wall moves on every beat, and the movement IS the beat.
        /// </summary>
        OneStepPerBeat,

        /// <summary>
        /// A whole sweep per beat.
        ///
        /// One beat runs the entire fill quickly, one picture after another, and
        /// then holds full until the next beat runs the entire clear. Two beats
        /// for a complete cycle however many pictures are in it.
        ///
        /// The punchier reading: the beat is the moment a sweep is LAUNCHED
        /// rather than the moment the wall moves, in the same way a Starburst is
        /// launched by a beat and then plays out on its own.
        /// </summary>
        WholeSweepPerBeat
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
        /// How much of a stepped animation one beat is worth. See FillPacing.
        ///
        /// Used only by the Fill and Clear effects at present, so it sits closer
        /// to MeteorTailLength than to BeatSource in spirit. It is phrased in
        /// general terms because the question - does a beat advance one picture,
        /// or launch a whole run of them - is one any stepped effect could face,
        /// and answering it the same way everywhere is better than each effect
        /// inventing its own control.
        ///
        /// Defaults to one step per beat, which is the slower and more
        /// deliberate of the two.
        /// </summary>
        public FillPacing FillPacing { get; set; } = FillPacing.OneStepPerBeat;

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
                BeatSource = BeatSource,
                FillPacing = FillPacing
            };
        }
    }
}
