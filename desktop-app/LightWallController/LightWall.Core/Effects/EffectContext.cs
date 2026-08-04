using System;

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
        public EffectContext(double timeSeconds, EffectParameters parameters, int sessionSeed)
        {
            TimeSeconds = timeSeconds;
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _sessionSeed = sessionSeed;
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
