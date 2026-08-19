namespace LightWall.Core.Audio
{
    /// <summary>
    /// The beat-related part of an audio snapshot, bundled together.
    ///
    /// Exists so the AudioFeatures constructor does not grow to a dozen loose
    /// arguments. Several doubles in a row are easy to pass in the wrong order
    /// and impossible to spot when you have; naming them once here means they
    /// travel together and cannot be shuffled.
    ///
    /// A "record" is a compact way to declare a small type that exists to hold a
    /// few values. The compiler writes the constructor, the properties and the
    /// comparison logic, and the values cannot be changed after creation - which
    /// matters here, since these end up inside a snapshot shared between threads.
    ///
    /// TWO KINDS OF BEAT, DELIBERATELY BOTH
    ///
    /// SecondsSinceBeat comes from beats actually heard. Honest, and right for
    /// reacting to drum hits - but it goes quiet when the music does.
    ///
    /// SecondsSincePulse comes from a metronome running at the estimated tempo.
    /// It keeps counting through a breakdown where nothing is being played.
    ///
    /// Different effects want different ones, so both are carried.
    /// </summary>
    /// <param name="SecondsSinceBeat">
    /// How long ago a beat was actually detected. AudioFeatures.NoBeatYet when
    /// none has been heard.
    /// </param>
    /// <param name="BeatCount">Beats detected since capture started.</param>
    /// <param name="TempoBpm">Estimated tempo, or 0 when unknown.</param>
    /// <param name="TempoConfidence">
    /// How consistent recent beats have been, 0 to 1. Falls during quiet
    /// passages while the tempo itself is held.
    /// </param>
    /// <param name="SecondsSincePulse">
    /// How long ago the metronome last struck. AudioFeatures.NoBeatYet before
    /// the tempo is known.
    /// </param>
    /// <param name="PulseCount">Metronome pulses since counting began.</param>
    /// <param name="BeatPhase">
    /// How far through the current beat, from 0 (just landed) to 1 (next one
    /// due). For effects that want to sweep or fade across a beat rather than
    /// merely blink on it.
    /// </param>
    public readonly record struct BeatInfo(
        double SecondsSinceBeat,
        int BeatCount,
        double TempoBpm,
        double TempoConfidence,
        double SecondsSincePulse,
        int PulseCount,
        double BeatPhase,
        double TempoStability)
    {
        /// <summary>
        /// The state before anything has been heard.
        /// </summary>
        public static BeatInfo None => new(
            AudioFeatures.NoBeatYet,
            0,
            0.0,
            0.0,
            AudioFeatures.NoBeatYet,
            0,
            0.0,
            0.0);
    }
}
