namespace LightWall.Core.Audio
{
    /// <summary>
    /// The beat-related part of an audio snapshot, bundled together.
    ///
    /// Exists purely to stop the AudioFeatures constructor growing to ten
    /// arguments. Four loose doubles in a row are easy to pass in the wrong
    /// order and impossible to spot when you have; naming them once here means
    /// they travel together and cannot be shuffled.
    ///
    /// A "record" is a compact way to declare a small type that exists to hold a
    /// few values. The compiler writes the constructor, the properties and the
    /// comparison logic, and the values cannot be changed after creation - which
    /// matters here, since these end up inside a snapshot shared between threads.
    /// </summary>
    /// <param name="SecondsSinceBeat">
    /// How long ago the last beat was detected. AudioFeatures.NoBeatYet when
    /// none has been heard.
    /// </param>
    /// <param name="BeatCount">Beats detected since capture started.</param>
    /// <param name="TempoBpm">Estimated tempo, or 0 when unknown.</param>
    /// <param name="TempoConfidence">How consistent recent beats have been, 0 to 1.</param>
    public readonly record struct BeatInfo(
        double SecondsSinceBeat,
        int BeatCount,
        double TempoBpm,
        double TempoConfidence)
    {
        /// <summary>
        /// The state before any beat has been heard.
        /// </summary>
        public static BeatInfo None => new(AudioFeatures.NoBeatYet, 0, 0.0, 0.0);
    }
}
