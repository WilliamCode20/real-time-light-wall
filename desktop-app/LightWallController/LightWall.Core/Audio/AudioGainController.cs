using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Makes the wall respond the same way regardless of how loud the computer's
    /// volume is set.
    ///
    /// THE PROBLEM
    ///
    /// Measured loudness depends on the volume knob as much as on the music.
    /// Halve the system volume and every reading halves, so a wall driven
    /// directly from those readings would only bump half as high - for no
    /// musical reason at all.
    ///
    /// Worse, it makes the whole thing feel unreliable. Someone sets up the wall,
    /// it looks great, then a DJ adjusts their output level and it stops
    /// working properly.
    ///
    /// THE FIX: MEASURE AGAINST RECENT HISTORY, NOT AGAINST FULL SCALE
    ///
    /// Instead of asking "how loud is this compared to the loudest sound
    /// possible?", ask "how loud is this compared to the loudest thing I have
    /// heard in the last few seconds?".
    ///
    /// A reference level follows the peaks: it jumps up instantly whenever
    /// something louder arrives, and drifts slowly back down when nothing does.
    /// Everything is then measured as a fraction of that.
    ///
    /// The volume knob affects the music and the reference equally, so it
    /// cancels out. Quiet music fills the wall just as well as loud music.
    ///
    /// This is the same idea as automatic gain control in a camera, which is why
    /// a dim room still produces a bright picture.
    ///
    /// THE LIMIT WORTH KNOWING
    ///
    /// This cannot tell quiet music from loud music played quietly. Left alone
    /// during a soft passage it will keep winding the gain up until the wall is
    /// bumping to almost nothing. That is what MinimumReference is for: below a
    /// certain loudness we refuse to amplify further, so real silence stays dark
    /// rather than turning room hiss into a light show.
    /// </summary>
    public sealed class AudioGainController
    {
        /// <summary>
        /// The loudest level seen recently, which everything is measured against.
        /// </summary>
        private double _reference;

        /// <summary>
        /// How long the reference takes to drift back down when nothing loud
        /// happens, in seconds.
        ///
        /// Short values adapt quickly but make the wall's response wander during
        /// a quiet passage. Long values are steadier but take a while to adjust
        /// after a real change in volume.
        ///
        /// A few seconds is a reasonable compromise: long enough to sit still
        /// through the quiet part of a bar, short enough to adapt within a
        /// phrase.
        /// </summary>
        public double ReferenceDecaySeconds { get; set; } = 4.0;

        /// <summary>
        /// The quietest reference allowed - the point below which we stop
        /// amplifying.
        ///
        /// Without this, silence would be divided by a vanishingly small
        /// reference and blown up to full scale, so the wall would strobe wildly
        /// to nothing at all between tracks.
        /// </summary>
        public double MinimumReference { get; set; } = 0.15;

        /// <summary>
        /// A manual multiplier on top of the automatic adjustment, for taste.
        ///
        /// 1.0 leaves it as measured. Higher makes the wall bump harder and
        /// spend more time near the top; lower makes it more restrained. This is
        /// what the Sensitivity slider controls.
        /// </summary>
        public double Gain { get; set; } = 1.0;

        /// <summary>
        /// How sharply the response separates quiet from loud.
        ///
        /// The normalised level is raised to this power. Above 1, quiet moments
        /// are pushed further down while loud ones stay high, which spreads the
        /// bars over more of the wall and makes the movement punchier:
        ///
        ///   at 1.0    0.5 stays 0.5          gentle, everything mid-height
        ///   at 1.5    0.5 becomes 0.35       noticeably punchier
        ///   at 2.5    0.5 becomes 0.18       only the loud parts reach high
        ///
        /// Below 1 it does the opposite, compressing everything towards the top.
        /// </summary>
        public double Contrast { get; set; } = 1.6;

        /// <summary>
        /// The level below which we report nothing at all rather than
        /// amplifying.
        ///
        /// WHY THIS IS NEEDED - a real cause of flickering
        ///
        /// The automatic adjustment divides by a reference that shrinks when
        /// nothing loud is happening. For a band with almost no content - the
        /// sub-bass on a track with no deep bass, say - that means dividing a
        /// tiny number by another tiny number.
        ///
        /// The result swings wildly on noise that is inaudible. A band carrying
        /// nothing musical at all ends up shimmering between one row and none,
        /// several times a second, and the wall looks like static.
        ///
        /// Below this level we simply say zero. A band with nothing in it stays
        /// dark rather than being amplified into a light show.
        ///
        /// This is measured before the automatic adjustment, so it is an
        /// absolute judgement about whether there is anything there - which is
        /// exactly what it should be.
        /// </summary>
        public double NoiseGate { get; set; } = 0.06;

        /// <summary>
        /// The current reference level. Useful for diagnostics, and for showing
        /// how hard the automatic adjustment is working.
        /// </summary>
        public double Reference => _reference;

        /// <summary>
        /// Converts an absolute loudness into a 0-to-1 value where the recent
        /// loudest moments reach the top.
        /// </summary>
        /// <param name="level">Absolute loudness from 0 to 1.</param>
        /// <param name="deltaSeconds">Time since the previous reading.</param>
        public double Normalise(double level, double deltaSeconds)
        {
            // Nothing worth amplifying. Say so plainly rather than dividing one
            // tiny number by another and producing noise. See NoiseGate.
            //
            // The reference is still allowed to decay below, so that when sound
            // does return the adjustment is not stuck holding an old, loud
            // reference and reporting near-silence.
            bool belowGate = level < NoiseGate;

            if (level > _reference)
            {
                // Something louder than anything recent. Jump straight to it,
                // so a sudden loud passage does not clip the wall at full
                // height for several seconds while the reference catches up.
                _reference = level;
            }
            else
            {
                // Nothing loud is happening, so slowly forget how loud it used
                // to be. Drifting towards the minimum rather than towards the
                // current level means the reference represents "the loudest
                // thing lately", which is what we want to measure against.
                _reference = MoveTowards(
                    _reference,
                    MinimumReference,
                    ReferenceDecaySeconds,
                    deltaSeconds);
            }

            if (belowGate)
            {
                return 0.0;
            }

            // Never divide by less than the floor. This is what stops silence
            // being amplified into a full-scale light show.
            double reference = Math.Max(_reference, MinimumReference);

            double normalised = (level / reference) * Gain;
            normalised = Math.Clamp(normalised, 0.0, 1.0);

            // Shape the response so quiet and loud are further apart.
            return Math.Pow(normalised, Contrast);
        }

        /// <summary>
        /// Forgets all history and starts adjusting from scratch.
        /// </summary>
        public void Reset()
        {
            _reference = 0.0;
        }

        /// <summary>
        /// Eases one value towards another over time.
        ///
        /// The same exponential easing used by AudioLevelTracker: each step
        /// closes a fraction of the remaining gap, which keeps the result
        /// independent of how often it is called.
        /// </summary>
        private static double MoveTowards(
            double current,
            double target,
            double timeConstantSeconds,
            double deltaSeconds)
        {
            if (timeConstantSeconds <= 0.0)
            {
                return target;
            }

            double safeDelta = Math.Clamp(deltaSeconds, 0.0, 1.0);
            double fraction = 1.0 - Math.Exp(-safeDelta / timeConstantSeconds);

            return current + ((target - current) * fraction);
        }
    }
}
