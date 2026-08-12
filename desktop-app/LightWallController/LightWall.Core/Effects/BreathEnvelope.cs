using System;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// The rise and fall behind every breathing effect: a level between 0 and 1
    /// that beats push upward and time lets back down.
    ///
    /// Three effects share this - Breathing, Wiggle Breathing and EQ Breathing.
    /// They differ only in what shape they draw at a given fullness, which is a
    /// few lines each. The timing underneath is the part with all the care in it,
    /// so it lives here once rather than three times.
    ///
    /// BEATS PUSH UP, AND NEVER PULL DOWN
    ///
    /// A beat raises the level from wherever it currently is. Nothing here ever
    /// sets it back to zero.
    ///
    /// That sounds like a small distinction and it is the whole feel of these
    /// effects. The first version of Breathing worked its height out purely from
    /// how long ago the last beat was - a clean idea with a real virtue, which
    /// was that it needed no memory at all and was a pure function of the moment
    /// it was asked about.
    ///
    /// It looked wrong in practice, for a reason only visible with music playing.
    /// Beats frequently arrive before the previous breath has finished sinking.
    /// Because the height came from "time since the last beat", a new beat meant
    /// a time of zero, which meant the floor - so the line SNAPPED down and
    /// started again, cutting off the tail of the movement. A run of quick beats
    /// made it slam up and down repeatedly instead of hovering near the top and
    /// breathing there, which is what a chest actually does when someone is
    /// breathing hard.
    ///
    /// The general lesson, worth applying to anything beat-driven added later: a
    /// beat should rarely reset an animation to its start. Beats arrive part way
    /// through the previous movement far more often than they do not.
    ///
    /// WHY HOLDING STATE IS SAFE
    ///
    /// The rule effects follow is that asking about the same moment twice gives
    /// the same answer, so that redrawing far more often than anything changes
    /// cannot make the picture flicker. That still holds here.
    ///
    /// Nothing moves unless time has actually advanced: the level is stepped by
    /// the gap since the previous frame, so a second frame at the same moment
    /// steps by nothing. And a beat only registers when the beat COUNT changes,
    /// so the same beat cannot be counted twice however often it is looked at.
    ///
    /// A NOTE ON THE SPEED SLIDER
    ///
    /// It affects how briskly the level rises and falls, because the movement is
    /// stepped by elapsed effect time and the slider scales that. The beats
    /// themselves still arrive from the music, so away from 100% the breath and
    /// the beat drift apart - fast enough and the level reaches the top and
    /// waits, slow enough and it never gets there.
    ///
    /// That is a real control rather than a fault: it is how punchy or how smooth
    /// the effect feels.
    /// </summary>
    public sealed class BreathEnvelope
    {
        /// <summary>
        /// How long one breath lasts when no tempo has been worked out yet.
        ///
        /// Half a second is 120 beats a minute, a fair guess for most music and
        /// only in force for the first few seconds of a track.
        /// </summary>
        private const double AssumedBeatSeconds = 0.5;

        /// <summary>
        /// What share of a beat is spent rising, the rest being spent falling.
        ///
        /// Slightly less than half, because that is what real breathing does -
        /// the intake is quicker than the release. It also matters musically: the
        /// rise is the part that should feel connected to the beat, so getting to
        /// the top of it sooner keeps the movement and the drum together.
        /// </summary>
        private const double InhaleShare = 0.35;

        /// <summary>
        /// The largest step taken in one frame, in seconds.
        ///
        /// Without this, a stall - a debugger pause, a laptop waking up - would
        /// arrive as one enormous gap and jump the level straight to an end stop.
        /// The engine caps its own steps for the same reason.
        /// </summary>
        private const double LargestStepSeconds = 0.25;

        /// <summary>Whether the level is currently rising.</summary>
        private bool _rising;

        /// <summary>The effect time at the previous frame.</summary>
        private double _lastTimeSeconds;

        /// <summary>
        /// How full, from 0 (resting) to 1 (fully drawn in), as a straight
        /// level with no smoothing applied.
        /// </summary>
        public double Inflation { get; private set; }

        /// <summary>
        /// How full, softened so the movement starts and stops gently.
        ///
        /// THIS IS THE ONE TO DRAW WITH. Without the softening the shape would
        /// change height at a constant rate and stop dead at the top, which reads
        /// as mechanical. Breathing slows as the chest fills.
        ///
        /// Smoothed on the way out rather than while being stored, so that a beat
        /// arriving part way through a fall picks up from exactly where the shape
        /// is rather than from a different point on the curve.
        /// </summary>
        public double EasedInflation => Ease(Inflation);

        /// <summary>
        /// Which beat the current breath belongs to.
        ///
        /// Effects that look different from one breath to the next work their
        /// appearance out from this number, so that the same breath always looks
        /// the same however many times it is drawn.
        /// </summary>
        public int BeatNumber { get; private set; }

        /// <summary>
        /// Moves the level on by however much time has passed, and lets any new
        /// beat push it upward.
        /// </summary>
        public void Advance(EffectContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            double now = context.TimeSeconds;

            // Effect time restarts from zero when an effect is reselected. Time
            // running backwards means a fresh start rather than an enormous
            // negative step.
            if (now < _lastTimeSeconds)
            {
                Forget();
            }

            double elapsed = Math.Min(now - _lastTimeSeconds, LargestStepSeconds);
            _lastTimeSeconds = now;

            // A COUNT rather than the time since the last beat, deliberately. A
            // time has to be caught inside some window, and the engine reads
            // audio on its own schedule - a short window can be stepped clean
            // over and a long one can be seen twice. A count that has changed
            // means exactly one thing however often it is read, which is also
            // what makes drawing the same moment twice safe.
            //
            // Which count it is - beats actually heard, or the tempo metronome -
            // is the user's choice. See BeatSource.
            if (context.BeatCount != BeatNumber)
            {
                BeatNumber = context.BeatCount;

                // Start rising from wherever the level currently is. Nothing here
                // resets it, which is the point of the whole arrangement.
                _rising = true;
            }

            if (elapsed <= 0.0)
            {
                return;
            }

            double beatSeconds = AssumedBeatSeconds;

            if (context.Audio.TempoBpm > 0.0)
            {
                beatSeconds = 60.0 / context.Audio.TempoBpm;
            }

            if (_rising)
            {
                Inflation += elapsed / (beatSeconds * InhaleShare);

                if (Inflation >= 1.0)
                {
                    Inflation = 1.0;
                    _rising = false;
                }

                return;
            }

            Inflation -= elapsed / (beatSeconds * (1.0 - InhaleShare));

            if (Inflation < 0.0)
            {
                Inflation = 0.0;
            }
        }

        /// <summary>
        /// Returns to a resting level, for when nothing is being listened to.
        /// </summary>
        public void Forget()
        {
            Inflation = 0.0;
            _rising = false;
            BeatNumber = 0;
            _lastTimeSeconds = 0.0;
        }

        /// <summary>
        /// Softens a straight 0-to-1 level into something that starts and ends
        /// gently.
        ///
        /// The curve is 3x squared minus 2x cubed, the usual way of doing this:
        /// it passes through 0 and 1 at the ends and is flat at both, so there is
        /// no sudden change of pace where rising turns into falling.
        /// </summary>
        private static double Ease(double value)
        {
            return value * value * (3.0 - (2.0 * value));
        }
    }
}
