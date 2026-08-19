using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Turns a stream of raw loudness readings into a smooth level suitable for
    /// driving lights.
    ///
    /// THE PROBLEM THIS SOLVES
    ///
    /// Raw loudness readings arrive roughly a hundred times a second and jump
    /// around constantly, because music is made of individual waves rather than
    /// a steady glow. Driving anything visual straight from them produces a
    /// flickering mess.
    ///
    /// The obvious fix - average the last few readings - is wrong in a way worth
    /// understanding. It smooths the sharp attack of a drum hit just as much as
    /// it smooths the jitter, and the sharp attack is the part you actually want
    /// the wall to show.
    ///
    /// ATTACK AND RELEASE
    ///
    /// So the smoothing is deliberately lopsided:
    ///
    ///   rising  - move almost instantly (fast attack)
    ///   falling - ease down gently (slow release)
    ///
    /// A drum hit therefore snaps the level up immediately and then lets it
    /// decay, which is exactly how a light responding to a beat should behave.
    /// Meanwhile the constant small dips between waves get smoothed away,
    /// because falling is slow.
    ///
    /// This is the same trick used by every compressor, limiter and VU meter in
    /// audio, for the same reason.
    /// </summary>
    public sealed class AudioLevelTracker
    {
        /// <summary>
        /// The current smoothed level, from 0 to 1.
        /// </summary>
        private double _level;

        /// <summary>
        /// Removes the effect of the system volume setting and shapes the
        /// response. See AudioGainController.
        /// </summary>
        public AudioGainController Gain { get; } = new();

        /// <summary>
        /// How quickly the level rises towards a louder reading, in seconds.
        ///
        /// Very short on purpose. A beat should land on the wall at the moment
        /// it lands in the music, not a fraction of a second afterwards - the
        /// eye notices that delay easily.
        /// </summary>
        public double AttackSeconds { get; set; } = 0.01;

        /// <summary>
        /// How slowly the level falls away when the sound gets quieter, in
        /// seconds.
        ///
        /// Much longer than the attack. This is what turns a sharp drum hit into
        /// a visible pulse with a tail, instead of a single frame flicker that
        /// the eye can barely register.
        ///
        /// Longer values feel smoother and more atmospheric; shorter values feel
        /// punchier and more aggressive. A quarter of a second is a reasonable
        /// middle ground and a good candidate for a user-facing control later.
        /// </summary>
        public double ReleaseSeconds { get; set; } = 0.25;

        /// <summary>
        /// The quietest sound that registers at all, in decibels.
        ///
        /// See AudioSampleMath.LinearToNormalisedDecibels for why this matters.
        /// Around -60 suits music, where anything quieter is silence or room
        /// noise.
        /// </summary>
        public double MinimumDecibels { get; set; } = -60.0;

        /// <summary>
        /// The current smoothed level, from 0 to 1.
        /// </summary>
        public double Level => _level;

        /// <summary>
        /// Feeds in a new loudness reading and returns the resulting snapshot.
        /// </summary>
        /// <param name="rms">Raw average loudness of the latest buffer, 0 to 1.</param>
        /// <param name="peak">Loudest single sample in that buffer, 0 to 1.</param>
        /// <param name="deltaSeconds">
        /// How long since the previous reading. Passed in rather than assumed,
        /// for the same reason the engine takes measured elapsed time: audio
        /// buffers do not arrive at perfectly even intervals, and smoothing that
        /// assumed they did would speed up and slow down with the buffer size.
        /// </param>
        /// <param name="bandLevels">
        /// Per-band levels to carry in the snapshot, or null for none. Supplied
        /// by the caller because banding is a separate concern from level
        /// tracking - this class is also used once per band, where passing bands
        /// again would be circular.
        /// </param>
        public AudioFeatures Update(
            double rms,
            double peak,
            double deltaSeconds,
            double[]? bandLevels = null,
            BeatInfo? beat = null)
        {
            // Convert to something that matches how loudness feels before
            // smoothing, not after. Smoothing a linear value and converting
            // afterwards would make the smoothing itself behave differently at
            // different volumes.
            double target = AudioSampleMath.LinearToNormalisedDecibels(rms, MinimumDecibels);

            // Rising and falling are treated differently - see the class notes.
            double timeConstant = target > _level ? AttackSeconds : ReleaseSeconds;

            _level = MoveTowards(_level, target, timeConstant, deltaSeconds);

            // Then remove the influence of the volume knob, so the wall behaves
            // the same whether the computer is at half volume or full.
            double normalised = Gain.Normalise(_level, deltaSeconds);

            BeatInfo beatInfo = beat ?? BeatInfo.None;

            return new AudioFeatures(
                rms,
                peak,
                _level,
                normalised,
                bandLevels ?? new double[FrequencyBands.Count],
                isSilent: false,
                beatInfo.SecondsSinceBeat,
                beatInfo.BeatCount,
                beatInfo.TempoBpm,
                beatInfo.TempoConfidence,
                beatInfo.SecondsSincePulse,
                beatInfo.PulseCount,
                beatInfo.BeatPhase,
                beatInfo.TempoStability);
        }

        /// <summary>
        /// Records that no audio arrived at all, letting the level decay away.
        ///
        /// Windows sends no buffers when nothing is playing, so silence does not
        /// arrive as a stream of zeros - it arrives as nothing at all. Without
        /// this, the level would simply freeze wherever it was when the music
        /// stopped, and the wall would hold a pose forever.
        ///
        /// The level is eased down rather than cut to zero, so pausing a track
        /// fades the wall out instead of snapping it dark.
        /// </summary>
        public AudioFeatures UpdateSilent(
            double deltaSeconds,
            double[]? bandLevels = null,
            BeatInfo? beat = null)
        {
            _level = MoveTowards(_level, 0.0, ReleaseSeconds, deltaSeconds);

            double normalised = Gain.Normalise(_level, deltaSeconds);

            BeatInfo beatInfo = beat ?? BeatInfo.None;

            return new AudioFeatures(
                0.0,
                0.0,
                _level,
                normalised,
                bandLevels ?? new double[FrequencyBands.Count],
                isSilent: true,
                beatInfo.SecondsSinceBeat,
                beatInfo.BeatCount,
                beatInfo.TempoBpm,
                beatInfo.TempoConfidence,
                beatInfo.SecondsSincePulse,
                beatInfo.PulseCount,
                beatInfo.BeatPhase,
                beatInfo.TempoStability);
        }

        /// <summary>
        /// Forgets everything and returns to silence.
        /// </summary>
        public void Reset()
        {
            _level = 0.0;
            Gain.Reset();
        }

        /// <summary>
        /// Eases one value towards another over time.
        ///
        /// HOW THIS WORKS
        ///
        /// Each step closes a fraction of the remaining gap rather than moving a
        /// fixed distance. That produces a natural-looking curve which moves
        /// quickly when far from the target and settles gently as it arrives -
        /// the same shape as anything decaying in the physical world.
        ///
        /// The time constant is roughly how long it takes to close about two
        /// thirds of the gap. The exponential is what makes the result
        /// independent of how often this is called: whether readings arrive
        /// every 5 milliseconds or every 20, the level takes the same real time
        /// to get where it is going.
        /// </summary>
        private static double MoveTowards(
            double current,
            double target,
            double timeConstantSeconds,
            double deltaSeconds)
        {
            // A time constant of zero means "no smoothing at all" - jump
            // straight there. Also guards against dividing by zero below.
            if (timeConstantSeconds <= 0.0)
            {
                return target;
            }

            // Negative or absurd time steps would produce nonsense. A long gap
            // is capped rather than honoured, for the same reason the engine
            // caps its own: a pause at a debugger breakpoint should not make the
            // level teleport.
            double safeDelta = Math.Clamp(deltaSeconds, 0.0, 1.0);

            double fraction = 1.0 - Math.Exp(-safeDelta / timeConstantSeconds);

            return current + ((target - current) * fraction);
        }
    }
}
