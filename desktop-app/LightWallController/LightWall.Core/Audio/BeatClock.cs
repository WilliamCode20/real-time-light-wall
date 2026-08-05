using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// A metronome that runs at the estimated tempo and keeps itself in step
    /// with the music.
    ///
    /// HOW THIS DIFFERS FROM ONSET DETECTION
    ///
    /// OnsetDetector reports beats it actually heard. That is honest and exactly
    /// right for reacting to drum hits, but real music does not oblige with a
    /// hit on every beat - drums drop out, a bar goes by on just a bass note,
    /// and the wall goes quiet with it.
    ///
    /// This does the opposite. Once the tempo is known it keeps counting whether
    /// or not anything is being played, so the pulse carries straight through
    /// the gaps. Detected beats are used to keep it aligned rather than to
    /// trigger it.
    ///
    /// Both are useful, for different things, which is why both exist.
    ///
    /// STAYING IN STEP
    ///
    /// Knowing the tempo is not enough - a metronome at exactly the right speed
    /// but starting half a beat late stays wrong forever. It also needs to know
    /// WHERE in the bar it is, which is called phase.
    ///
    /// So each detected beat nudges the phase a little toward where it should
    /// be, rather than snapping to it. Snapping would make the pulse lurch every
    /// time detection was slightly early or late, which is often. Nudging means
    /// a run of detections pulls it steadily into alignment while any single bad
    /// one barely moves it.
    ///
    /// This is the same idea as a phase-locked loop, and the same reason a good
    /// clock corrects itself gradually rather than jumping.
    /// </summary>
    public sealed class BeatClock
    {
        /// <summary>
        /// Where we are within the current beat, from 0 (just landed) to 1 (the
        /// next one is due).
        /// </summary>
        private double _phase;

        /// <summary>
        /// How much of the way to pull toward a detected beat, from 0 to 1.
        ///
        /// Low values ignore detection almost entirely and drift; high values
        /// lurch on every slightly-off detection. Around 0.15 pulls into
        /// alignment over a few beats while shrugging off individual mistakes.
        /// </summary>
        public double Correction { get; set; } = 0.15;

        /// <summary>
        /// How long each pulse lasts, as a fraction of the gap between beats.
        ///
        /// Expressed as a fraction rather than in seconds so the pulse keeps the
        /// same feel at any tempo - a fifth of a beat looks the same whether the
        /// music is fast or slow.
        /// </summary>
        public double PulseWidth { get; set; } = 0.22;

        /// <summary>
        /// The tempo being counted at, or 0 when it is not yet known.
        /// </summary>
        public double Bpm { get; private set; }

        /// <summary>
        /// How far through the current beat we are, from 0 to 1.
        ///
        /// Useful beyond flashing: an effect can use this to sweep, fade or
        /// travel exactly once per beat rather than merely blinking on it.
        /// </summary>
        public double Phase => _phase;

        /// <summary>
        /// True while the wall should be lit for this beat.
        /// </summary>
        public bool IsPulsing => Bpm > 0.0 && _phase < PulseWidth;

        /// <summary>
        /// How many pulses have happened since counting began.
        /// </summary>
        public int PulseCount { get; private set; }

        /// <summary>
        /// Moves the clock forward.
        /// </summary>
        /// <param name="deltaSeconds">Time since the previous update.</param>
        /// <param name="bpm">The current tempo estimate, or 0 if unknown.</param>
        public void Update(double deltaSeconds, double bpm)
        {
            Bpm = bpm;

            if (bpm <= 0.0 || deltaSeconds <= 0.0)
            {
                return;
            }

            // Beats per second, which is how much of a beat passes each second.
            double beatsPassed = deltaSeconds * (bpm / 60.0);

            _phase += beatsPassed;

            // Each time the phase passes 1 we have reached the next beat.
            //
            // A while loop rather than a single subtraction, so an unusually
            // long gap - a stall, or a laptop waking up - does not leave the
            // phase stuck above 1 and the pulse jammed on.
            while (_phase >= 1.0)
            {
                _phase -= 1.0;
                PulseCount++;
            }
        }

        /// <summary>
        /// Nudges the clock toward a beat that was actually heard.
        ///
        /// Called whenever OnsetDetector finds something. A detected beat means
        /// the phase should be 0 right now, so the difference from 0 is the
        /// error - and we correct a fraction of it rather than all of it.
        /// </summary>
        public void SyncToDetectedBeat()
        {
            if (Bpm <= 0.0)
            {
                return;
            }

            // How far off we are, as a value between -0.5 and +0.5.
            //
            // A phase of 0.9 means the next beat is nearly due, so we are 0.1
            // EARLY rather than 0.9 late - hence measuring from whichever end is
            // closer. Getting this wrong would drag the clock the long way round
            // and never settle.
            double error = _phase > 0.5 ? _phase - 1.0 : _phase;

            _phase -= error * Correction;

            // Correcting can push the phase outside its range at either end.
            if (_phase < 0.0)
            {
                _phase += 1.0;
            }
            else if (_phase >= 1.0)
            {
                _phase -= 1.0;
            }
        }

        /// <summary>
        /// Stops counting and forgets where it was.
        /// </summary>
        public void Reset()
        {
            _phase = 0.0;
            Bpm = 0.0;
            PulseCount = 0;
        }
    }
}
