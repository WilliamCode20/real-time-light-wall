using System;
using LightWall.Core.Audio;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Everything an effect needs to know in order to draw one frame.
    ///
    /// When the engine wants a new frame, it builds one of these and hands it to
    /// the active effect. The effect reads from it and draws accordingly.
    ///
    /// THE BIG IDEA: TIME, NOT FRAME NUMBER
    ///
    /// The most important value in here is TimeSeconds.
    ///
    /// The old version of this code asked effects "what does step 47 look like?"
    /// That worked, but it tied every animation's appearance to how often the
    /// timer happened to fire. Speeding the timer up did not just play the
    /// animation faster - it changed what the animation actually was.
    ///
    /// Now we ask "what does the wall look like 3.2 seconds in?" instead.
    ///
    /// That single change buys us a lot:
    ///
    /// - Animation speed and screen refresh rate become independent. We can
    ///   redraw the simulator 60 times a second while a sweep still advances at
    ///   its own designed pace.
    /// - The simulator and the physical wall can run at different update rates
    ///   and still show the same thing at the same moment. The wall will need a
    ///   slower rate than the screen, because the relays can only switch so fast.
    /// - Music sync becomes possible later. Beats happen at points in TIME, not
    ///   at frame numbers, so time is the only thing we could sync against.
    /// </summary>
    public sealed class EffectContext
    {
        /// <summary>
        /// Used to scramble the step number into a seed. Any odd number works;
        /// this one is a prime, which spreads nearby step numbers further apart
        /// so consecutive steps do not produce similar-looking randomness.
        /// </summary>
        private const int SeedScramble = 397;

        /// <summary>
        /// A number chosen once when an effect starts, used to make its random
        /// behavior different each time you run it.
        /// </summary>
        private readonly int _sessionSeed;

        /// <summary>
        /// Creates the information packet for one frame.
        /// </summary>
        /// <param name="timeSeconds">
        /// How long the current effect has been running, in seconds.
        /// This is "effect time", which already has the speed slider applied to
        /// it, so effects never need to think about speed themselves.
        /// </param>
        /// <param name="parameters">The current slider settings.</param>
        /// <param name="sessionSeed">
        /// Makes random effects look different on each run. Pass the same value
        /// twice and you get the same visual sequence twice, which is exactly
        /// what tests want.
        /// </param>
        /// <param name="audio">
        /// What the music was doing at this instant. Defaults to silence, so
        /// every existing caller that does not care about audio keeps working
        /// unchanged.
        /// </param>
        /// <param name="isAudioActive">
        /// Whether anything is actually listening.
        ///
        /// This is deliberately separate from "the level is zero". An effect
        /// needs to tell the difference between nobody having started audio
        /// capture at all — in which case it should carry on doing something
        /// watchable — and capture running with the music paused, in which case
        /// going dark is the correct and honest response.
        /// </param>
        public EffectContext(
            double timeSeconds,
            EffectParameters parameters,
            int sessionSeed,
            AudioFeatures? audio = null,
            bool isAudioActive = false)
        {
            TimeSeconds = timeSeconds;
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _sessionSeed = sessionSeed;
            Audio = audio ?? AudioFeatures.Silence;
            IsAudioActive = isAudioActive;
        }

        /// <summary>
        /// How long the current effect has been running, in seconds.
        ///
        /// This counts "effect time", not wall-clock time. If the speed slider
        /// is at 200%, this advances twice as fast as real seconds do.
        /// </summary>
        public double TimeSeconds { get; }

        /// <summary>
        /// The current slider settings the user can adjust while watching.
        /// </summary>
        public EffectParameters Parameters { get; }

        /// <summary>
        /// What the music is doing right now.
        ///
        /// This is the point the whole audio side of the project has been
        /// building towards: effects read this exactly the way they read
        /// TimeSeconds, and neither they nor anything around them needs to know
        /// that it came from Windows, or WASAPI, or a sound card.
        ///
        /// Always safe to read. These snapshots can never change once created,
        /// so this is a complete picture of one instant rather than something
        /// being rewritten underneath.
        /// </summary>
        public AudioFeatures Audio { get; }

        /// <summary>
        /// True when audio capture is running.
        ///
        /// Distinct from the level being zero, and the difference matters. An
        /// effect should behave differently when nobody has started listening
        /// (carry on doing something watchable) than when it is listening and
        /// the music has stopped (go dark, honestly).
        /// </summary>
        public bool IsAudioActive { get; }

        /// <summary>
        /// How many beats have happened, counting whichever kind of beat the
        /// user has asked for.
        ///
        /// THIS IS THE ONE FOR EFFECTS THAT DO SOMETHING ONCE PER BEAT.
        ///
        /// A count rather than a time, and that matters. A time would have to be
        /// caught inside some window after the beat, and the engine reads audio
        /// on its own schedule - so a short window could be stepped clean over
        /// and a long one could be seen twice and counted as two beats. A count
        /// that has changed means exactly one thing, however often it is read.
        ///
        /// Reading this rather than Audio.BeatCount is what lets the choice
        /// between real beats and the metronome be made once, in the interface,
        /// instead of in every effect. See BeatSource.
        /// </summary>
        public int BeatCount =>
            Parameters.BeatSource == BeatSource.Tempo
                ? Audio.PulseCount
                : Audio.BeatCount;

        /// <summary>
        /// How long ago the last beat was, counting whichever kind of beat the
        /// user has asked for.
        ///
        /// For effects that stay lit for a moment after each beat rather than
        /// doing something once on it. Where BeatCount is right for "start
        /// something new", this is right for "be bright just after".
        /// </summary>
        public double SecondsSinceBeat =>
            Parameters.BeatSource == BeatSource.Tempo
                ? Audio.SecondsSincePulse
                : Audio.SecondsSinceBeat;

        /// <summary>
        /// Converts the current time into a whole step number at whatever pace
        /// the calling effect wants.
        ///
        /// Some effects look best when they move continuously and smoothly.
        /// Others - anything involving randomness or a chunky, deliberate
        /// rhythm - look best when they change in distinct steps.
        ///
        /// Example:
        /// GetStep(8) means "advance 8 times per second", so at 1.5 seconds in
        /// this returns step 12.
        ///
        /// This is what lets a sparkle effect keep its snappy 8-changes-a-second
        /// feel even though the screen behind it is redrawing 60 times a second.
        /// </summary>
        public int GetStep(double stepsPerSecond)
        {
            if (stepsPerSecond <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stepsPerSecond),
                    "Steps per second must be greater than zero.");
            }

            return (int)Math.Floor(TimeSeconds * stepsPerSecond);
        }

        /// <summary>
        /// Creates a random number generator that is tied to a specific step.
        ///
        /// WHY THIS EXISTS - this one is subtle but important.
        ///
        /// The simulator redraws far more often than a sparkle effect actually
        /// changes. During a single step, an effect may be asked to draw the
        /// exact same moment several times over.
        ///
        /// If the effect used one shared, ever-advancing random generator, it
        /// would produce different sparkles every single redraw, and the wall
        /// would dissolve into a flickering mess instead of showing steady
        /// sparkles that change 8 times a second.
        ///
        /// By deriving the randomness from the step number, asking for "step 12"
        /// always gives back the same sparkles. The picture holds still until
        /// the step actually advances.
        ///
        /// A useful side effect: effects become predictable. The same time value
        /// always produces the same frame, which is what makes them testable and
        /// what would let us scrub back and forth along a timeline later.
        /// </summary>
        public Random CreateRandomForStep(int step)
        {
            return new Random(_sessionSeed + (step * SeedScramble));
        }
    }
}
