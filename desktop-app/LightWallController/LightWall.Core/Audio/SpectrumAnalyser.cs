using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Splits incoming sound into frequency bands and reports how strong each
    /// one is.
    ///
    /// This is what turns "the music is loud" into "there is a kick drum and no
    /// cymbals", which is the difference between a wall that throbs and a wall
    /// that looks like it is listening.
    ///
    /// HOW IT WORKS
    ///
    /// 1. Incoming audio is mixed down to one channel and kept in a rolling
    ///    buffer of the most recent 1024 samples.
    /// 2. That buffer is tapered at both ends, then transformed into frequency
    ///    content.
    /// 3. The result is averaged within each band's frequency range.
    /// 4. Each band then gets its own smoothing and its own automatic gain.
    ///
    /// WHY EACH BAND NEEDS ITS OWN AUTOMATIC GAIN - the important bit
    ///
    /// Music is not spread evenly across the spectrum. Bass typically carries
    /// enormously more energy than treble - often a hundred times more.
    ///
    /// Measured against one shared reference, the bass columns would sit at full
    /// height while the treble columns barely flickered. The wall would look
    /// broken, and no amount of overall sensitivity adjustment would fix it,
    /// because the problem is the ratio between bands rather than the level of
    /// any one.
    ///
    /// Giving each band its own reference means each column measures itself
    /// against its own recent history. A quiet hi-hat is loud *for a hi-hat* and
    /// lights its column accordingly. That is what makes all seven usable.
    ///
    /// ON LATENCY
    ///
    /// The window size is the deliberate trade here. 1024 samples is about 21
    /// milliseconds of sound at 48 kHz, so the wall is always reacting to
    /// something that finished about 21 ms ago.
    ///
    /// Halving it would halve that delay but also halve the frequency detail,
    /// leaving the lowest band resolved by barely one bin - and the bass is
    /// exactly where the detail matters most. 1024 is the point where both are
    /// still comfortable.
    ///
    /// The analysis runs on every incoming buffer, not once per window, so
    /// successive windows overlap heavily. That keeps the response quick without
    /// needing a shorter window.
    /// </summary>
    public sealed class SpectrumAnalyser
    {
        /// <summary>
        /// How many samples each analysis looks at. Must be a power of two.
        ///
        /// At 48 kHz this is about 21 ms of sound and gives roughly 47 Hz of
        /// frequency detail. See the class notes for why this particular
        /// compromise.
        /// </summary>
        public const int WindowSize = 1024;

        /// <summary>
        /// The rolling buffer of recent audio, oldest entry overwritten first.
        /// </summary>
        private readonly double[] _samples = new double[WindowSize];

        /// <summary>
        /// Where the next incoming sample will be written.
        /// </summary>
        private int _writePosition;

        /// <summary>
        /// The taper applied before transforming. Worked out once, since it
        /// never changes.
        /// </summary>
        private readonly double[] _window = FourierTransform.CreateHannWindow(WindowSize);

        /// <summary>Working space for the transform, reused every time.</summary>
        private readonly double[] _real = new double[WindowSize];

        /// <summary>Working space for the transform, reused every time.</summary>
        private readonly double[] _imaginary = new double[WindowSize];

        /// <summary>How strong each frequency came out, before banding.</summary>
        private readonly double[] _magnitudes = new double[WindowSize / 2];

        /// <summary>Smoothing for each band, so each responds independently.</summary>
        private readonly AudioLevelTracker[] _trackers;

        /// <summary>The latest level for each band, from 0 to 1.</summary>
        private readonly double[] _bandLevels = new double[FrequencyBands.Count];

        /// <summary>How settled the bands look. See Smoothing.</summary>
        private double _smoothing = 0.5;

        /// <summary>
        /// The band strengths straight out of the transform, before smoothing
        /// or automatic gain.
        ///
        /// Kept because beat detection needs them. Smoothing rounds off exactly
        /// the sharp rise an onset consists of, and automatic gain would make
        /// quiet noise look like a real jump - both fatal to spotting the moment
        /// a drum lands.
        /// </summary>
        private readonly double[] _rawStrengths = new double[FrequencyBands.Count];

        /// <summary>
        /// The quickest a band falls back, at zero smoothing. Snappy and a
        /// little twitchy.
        /// </summary>
        private const double MinimumReleaseSeconds = 0.10;

        /// <summary>
        /// The slowest a band falls back, at full smoothing. Flowing, and
        /// starting to lag behind fast music.
        /// </summary>
        private const double MaximumReleaseSeconds = 0.50;

        /// <summary>
        /// How much a band borrows from each of its neighbours at full
        /// smoothing.
        ///
        /// Kept well under half, because a band that takes more from its
        /// neighbours than from itself stops representing its own frequencies
        /// and the wall becomes one wide blur.
        /// </summary>
        private const double MaximumNeighbourBlend = 0.30;

        /// <summary>
        /// Creates an analyser for audio at a given sample rate.
        /// </summary>
        /// <param name="sampleRate">
        /// Samples per second. Needed to know which frequency each transform
        /// output corresponds to. Windows usually mixes at 48000.
        /// </param>
        public SpectrumAnalyser(int sampleRate = 48000)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            SampleRate = sampleRate;

            _trackers = new AudioLevelTracker[FrequencyBands.Count];

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                _trackers[band] = new AudioLevelTracker
                {
                    // A shallower floor than the overall level uses. Within a
                    // single band the useful range is narrower, so stretching it
                    // over 60 dB would leave everything bunched near the top.
                    MinimumDecibels = -45.0
                };
            }

            // Apply the default smoothing to every tracker.
            Smoothing = _smoothing;
        }

        /// <summary>
        /// Samples per second of the incoming audio.
        ///
        /// Settable because the default playback device can change while the app
        /// is running, and a different device may mix at a different rate.
        /// </summary>
        public int SampleRate { get; set; }

        /// <summary>
        /// How hard the bands are pushed, on top of their automatic adjustment.
        /// Mirrors the overall Sensitivity control.
        /// </summary>
        public double Sensitivity
        {
            get => _trackers[0].Gain.Gain;
            set
            {
                foreach (AudioLevelTracker tracker in _trackers)
                {
                    tracker.Gain.Gain = value;
                }
            }
        }

        /// <summary>
        /// How settled the bands look, from 0 (raw and twitchy) to 1 (slow and
        /// flowing). 0.5 is a reasonable middle.
        ///
        /// One control adjusting two things at once, because they are two halves
        /// of the same idea:
        ///
        /// - how long a band takes to fall back after a hit, which smooths each
        ///   column over TIME
        /// - how much neighbouring bands blend into each other, which smooths
        ///   the top edge ACROSS the wall
        ///
        /// The second is what gives an equaliser its familiar rolling curve
        /// rather than seven independent columns jumping about. It is also
        /// honest rather than decorative: neighbouring frequencies in real music
        /// genuinely are related, and the exact place we drew each band boundary
        /// was always somewhat arbitrary.
        ///
        /// The attack is deliberately NOT slowed by this. However smooth the
        /// wall should look, a drum hit should still land the moment it happens
        /// - slowing the rise makes the whole thing feel late rather than calm.
        /// </summary>
        public double Smoothing
        {
            get => _smoothing;
            set
            {
                _smoothing = Math.Clamp(value, 0.0, 1.0);

                // Falling gets slower as smoothing rises, from a snappy tenth of
                // a second up to a languid half second.
                double release = MinimumReleaseSeconds
                    + (_smoothing * (MaximumReleaseSeconds - MinimumReleaseSeconds));

                foreach (AudioLevelTracker tracker in _trackers)
                {
                    tracker.ReleaseSeconds = release;

                    // Rising stays quick regardless. A little slower than
                    // instant, which takes the edge off single-buffer spikes
                    // without making anything feel delayed.
                    tracker.AttackSeconds = 0.02;
                }
            }
        }

        /// <summary>
        /// Adds incoming audio to the rolling buffer.
        ///
        /// Channels are mixed together rather than analysed separately. For
        /// driving lights that is the right choice: a bass note panned to one
        /// side is still a bass note, and treating the sides differently would
        /// make the wall react to the stereo image rather than to the music.
        /// </summary>
        /// <param name="interleavedSamples">
        /// Samples as they arrive from the sound system, with the channels
        /// alternating: left, right, left, right, and so on.
        /// </param>
        /// <param name="channels">How many channels are interleaved.</param>
        public void AddSamples(ReadOnlySpan<float> interleavedSamples, int channels)
        {
            if (channels < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            for (int i = 0; i + channels <= interleavedSamples.Length; i += channels)
            {
                double total = 0.0;
                int counted = 0;

                for (int channel = 0; channel < channels; channel++)
                {
                    float value = interleavedSamples[i + channel];

                    // Skip anything a misbehaving driver might have produced.
                    // One infinity here would poison the entire transform and
                    // every band would read as nothing at all.
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        continue;
                    }

                    total += value;
                    counted++;
                }

                double mono = counted > 0 ? total / counted : 0.0;

                _samples[_writePosition] = mono;
                _writePosition = (_writePosition + 1) % WindowSize;
            }
        }

        /// <summary>
        /// Analyses whatever is currently in the buffer and updates every band.
        /// </summary>
        /// <param name="deltaSeconds">Time since the previous analysis.</param>
        /// <returns>
        /// A fresh array of band levels from 0 to 1, one per column.
        ///
        /// A new array each time rather than a reused one, because these end up
        /// inside an AudioFeatures snapshot that other threads read. Snapshots
        /// are only safe to share because nothing ever changes inside them.
        /// </returns>
        public double[] Analyse(double deltaSeconds)
        {
            ComputeMagnitudes();

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                double strength = GetBandStrength(band);

                // Keep the raw value for beat detection, which needs the sharp
                // edges that smoothing is about to round off.
                _rawStrengths[band] = strength;

                AudioFeatures features = _trackers[band].Update(strength, strength, deltaSeconds);

                _bandLevels[band] = features.NormalisedLevel;
            }

            return BlendNeighbours();
        }

        /// <summary>
        /// The band strengths straight out of the transform, before smoothing or
        /// automatic gain.
        ///
        /// Returns the working array rather than a copy, because the only caller
        /// is beat detection on the same thread, immediately after analysis.
        /// Not for handing to anything that keeps it.
        /// </summary>
        public double[] GetRawStrengths()
        {
            return _rawStrengths;
        }

        /// <summary>
        /// Lets every band decay when no audio is arriving at all.
        ///
        /// Windows sends nothing during silence rather than sending zeros, so
        /// without this the bands would freeze wherever the music left them.
        /// </summary>
        public double[] AnalyseSilence(double deltaSeconds)
        {
            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                AudioFeatures features = _trackers[band].UpdateSilent(deltaSeconds);

                _bandLevels[band] = features.NormalisedLevel;
            }

            return BlendNeighbours();
        }

        /// <summary>
        /// Lets each band borrow a little from the ones either side of it.
        ///
        /// This is what gives the wall a rolling curve along its top edge rather
        /// than seven independent columns jumping about. Without it, adjacent
        /// bars regularly differ by two or three rows and the shape reads as
        /// random spikes instead of a shape following the music.
        ///
        /// It is not merely decorative. Neighbouring frequencies in real music
        /// genuinely are related - a bass note has harmonics reaching up into
        /// the band above it - and the exact frequency at which we chose to draw
        /// each boundary was always somewhat arbitrary. Letting the bands bleed
        /// slightly acknowledges that the sound does not actually stop at the
        /// lines we drew.
        ///
        /// The outermost bands have only one neighbour each, so they lean on
        /// that one twice as hard. The alternative - treating the edges as
        /// bordering silence - would drag the first and last columns downward
        /// for no musical reason.
        /// </summary>
        private double[] BlendNeighbours()
        {
            var blended = new double[FrequencyBands.Count];

            double share = _smoothing * MaximumNeighbourBlend;

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                double below = band > 0 ? _bandLevels[band - 1] : _bandLevels[band + 1];
                double above = band < FrequencyBands.Count - 1
                    ? _bandLevels[band + 1]
                    : _bandLevels[band - 1];

                double own = 1.0 - (2.0 * share);

                blended[band] = Math.Clamp(
                    (_bandLevels[band] * own) + (below * share) + (above * share),
                    0.0,
                    1.0);
            }

            return blended;
        }

        /// <summary>
        /// Forgets all history and starts again.
        /// </summary>
        public void Reset()
        {
            Array.Clear(_samples);
            Array.Clear(_bandLevels);
            Array.Clear(_rawStrengths);
            _writePosition = 0;

            foreach (AudioLevelTracker tracker in _trackers)
            {
                tracker.Reset();
            }
        }

        /// <summary>
        /// Transforms the buffered audio into frequency strengths.
        /// </summary>
        private void ComputeMagnitudes()
        {
            // Copy out of the rolling buffer in time order. The buffer wraps
            // around, so the oldest sample is wherever the write position
            // currently points.
            for (int i = 0; i < WindowSize; i++)
            {
                int source = (_writePosition + i) % WindowSize;

                // Taper as we go. See FourierTransform.CreateHannWindow for why
                // this matters - without it a pure bass note would appear to
                // contain treble, and the high columns would twitch along with
                // the kick drum.
                _real[i] = _samples[source] * _window[i];
                _imaginary[i] = 0.0;
            }

            FourierTransform.Forward(_real, _imaginary);

            // Each output is a pair of numbers; how strong that frequency is
            // comes from their combined size, the same way the length of a line
            // comes from its horizontal and vertical extents.
            for (int bin = 0; bin < _magnitudes.Length; bin++)
            {
                double magnitude = Math.Sqrt(
                    (_real[bin] * _real[bin]) + (_imaginary[bin] * _imaginary[bin]));

                // Scale by the window length so the numbers do not depend on how
                // many samples were analysed.
                _magnitudes[bin] = magnitude / (WindowSize / 2.0);
            }
        }

        /// <summary>
        /// Measures how much energy falls within one band.
        ///
        /// WHY THIS ADDS RATHER THAN AVERAGES
        ///
        /// Averaging was tried first and was badly wrong, in a way worth
        /// recording because the reasoning sounded convincing.
        ///
        /// The argument for averaging was that the top band spans 10 kHz while
        /// the bottom spans 40 Hz, so adding would make the wide bands enormous
        /// purely because they are wide.
        ///
        /// What that misses is what happens to a single tone. A hi-hat might
        /// occupy two of the two hundred bins in the top band; averaged across
        /// all of them it is divided by a hundred and vanishes below the noise
        /// floor. In practice the treble columns read exactly zero and never
        /// moved at all.
        ///
        /// The concern about wide bands being louder was also unfounded, because
        /// every band is measured against its own recent history. Whatever scale
        /// a band naturally sits at is normalised away. There was nothing to
        /// correct for.
        ///
        /// Adding the squares and taking the root is the physically meaningful
        /// measure - it is the actual energy in that stretch of spectrum. A pure
        /// tone gives the same reading no matter how wide the band containing it
        /// happens to be, which is exactly the property that was missing.
        /// </summary>
        private double GetBandStrength(int band)
        {
            (int firstBin, int lastBin) = FrequencyBands.GetBinRange(band, WindowSize, SampleRate);

            double sumOfSquares = 0.0;

            for (int bin = firstBin; bin <= lastBin; bin++)
            {
                sumOfSquares += _magnitudes[bin] * _magnitudes[bin];
            }

            return Math.Clamp(Math.Sqrt(sumOfSquares), 0.0, 1.0);
        }
    }
}
