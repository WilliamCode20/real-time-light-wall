using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Spots the moment a new sound starts - a drum hit, a chord, a stab.
    ///
    /// WHAT AN ONSET IS, AND WHY IT IS NOT THE SAME AS "LOUD"
    ///
    /// A sustained bass note is loud the whole time it is held, but there is
    /// only one moment where it *begins*. That beginning is the onset, and it is
    /// what a beat actually consists of.
    ///
    /// Detecting loudness would flash the wall for the entire length of every
    /// note. Detecting onsets flashes it once, when the note arrives - which is
    /// what looks like it is following the music.
    ///
    /// HOW IT WORKS: SPECTRAL FLUX
    ///
    /// Comparing overall loudness between one moment and the next is a poor
    /// detector, because a new sound often arrives while something else is
    /// already playing and the total barely moves.
    ///
    /// Spectral flux compares each frequency band separately and adds up only
    /// the INCREASES. A hi-hat landing over a sustained bass note raises the
    /// high bands even though the low ones are unchanged, so the flux jumps even
    /// though the overall level did not.
    ///
    /// Only increases count. A sound stopping is not an onset, and counting
    /// decreases would produce a second spurious detection at the end of every
    /// note.
    ///
    /// WHY THE THRESHOLD MOVES
    ///
    /// A fixed threshold cannot work across different music. Set it for a dense
    /// mix and quiet passages never trigger; set it for a sparse one and a loud
    /// section triggers constantly.
    ///
    /// So the threshold follows the recent average flux. A beat is not "louder
    /// than some number" but "a bigger jump than this music has been making
    /// lately", which is much closer to what a listener actually notices.
    /// </summary>
    public sealed class OnsetDetector
    {
        /// <summary>
        /// How many recent flux readings the moving threshold is based on.
        ///
        /// At roughly a hundred readings a second this is about half a second -
        /// long enough to average over a beat or two, short enough to follow a
        /// change in the music.
        /// </summary>
        private const int HistoryLength = 48;

        /// <summary>Recent flux values, used to work out what is normal.</summary>
        private readonly double[] _history = new double[HistoryLength];

        /// <summary>Where the next reading goes in the ring of history.</summary>
        private int _historyPosition;

        /// <summary>How many readings have been recorded, up to HistoryLength.</summary>
        private int _historyCount;

        /// <summary>The band strengths from the previous reading.</summary>
        private double[] _previousBands = new double[FrequencyBands.Count];

        /// <summary>The flux from the previous reading, for peak-picking.</summary>
        private double _previousFlux;

        /// <summary>When the last beat was reported.</summary>
        private double _lastBeatSeconds = double.NegativeInfinity;

        /// <summary>
        /// How much bigger than the recent average a jump must be to count.
        ///
        /// Around 1.4 works well for most music. Lower finds more beats but
        /// starts reporting ordinary texture as beats; higher only catches the
        /// most obvious hits and misses softer ones.
        /// </summary>
        public double Sensitivity { get; set; } = 1.4;

        /// <summary>
        /// The shortest gap allowed between two beats, in seconds.
        ///
        /// WHY THIS IS NEEDED
        ///
        /// A single drum hit is not instantaneous - it rises over a few
        /// readings. Without a minimum gap, one kick drum would be reported as
        /// three or four beats in quick succession, which would ruin any attempt
        /// to work out the tempo.
        ///
        /// 0.12 seconds allows up to 500 beats a minute, far faster than any
        /// music, while comfortably covering the width of a single hit.
        /// </summary>
        public double MinimumSecondsBetweenBeats { get; set; } = 0.12;

        /// <summary>
        /// How much of the flux must come from a genuine signal before anything
        /// is reported at all.
        ///
        /// Guards against near-silence, where the recent average is almost zero
        /// and any tiny flicker looks enormous by comparison.
        /// </summary>
        public double MinimumFlux { get; set; } = 0.01;

        /// <summary>The most recent flux value. Useful for diagnostics.</summary>
        public double CurrentFlux { get; private set; }

        /// <summary>The threshold the flux was measured against.</summary>
        public double CurrentThreshold { get; private set; }

        /// <summary>
        /// Feeds in the latest frequency readings and reports whether a beat
        /// just started.
        /// </summary>
        /// <param name="bandStrengths">
        /// Raw band strengths, before smoothing and automatic gain. The raw
        /// values are used deliberately: smoothing rounds off exactly the sharp
        /// rise this is looking for, and automatic gain would make quiet noise
        /// look like a real jump.
        /// </param>
        /// <param name="nowSeconds">The current time.</param>
        /// <returns>True on the reading where a beat begins.</returns>
        public bool Update(double[] bandStrengths, double nowSeconds)
        {
            if (bandStrengths is null)
            {
                throw new ArgumentNullException(nameof(bandStrengths));
            }

            double flux = ComputeFlux(bandStrengths);

            CurrentFlux = flux;
            CurrentThreshold = ComputeThreshold();

            bool isBeat = IsOnset(flux, nowSeconds);

            if (isBeat)
            {
                _lastBeatSeconds = nowSeconds;
            }

            RecordFlux(flux);
            _previousFlux = flux;

            return isBeat;
        }

        /// <summary>
        /// Forgets everything and starts listening afresh.
        /// </summary>
        public void Reset()
        {
            Array.Clear(_history);
            Array.Clear(_previousBands);

            _historyPosition = 0;
            _historyCount = 0;
            _previousFlux = 0.0;
            _lastBeatSeconds = double.NegativeInfinity;

            CurrentFlux = 0.0;
            CurrentThreshold = 0.0;
        }

        /// <summary>
        /// Adds up how much each band grew since the previous reading.
        /// </summary>
        private double ComputeFlux(double[] bandStrengths)
        {
            double flux = 0.0;
            int count = Math.Min(bandStrengths.Length, _previousBands.Length);

            for (int band = 0; band < count; band++)
            {
                double change = bandStrengths[band] - _previousBands[band];

                // Only growth counts. A sound ending is not a sound starting,
                // and counting it would report a second beat at the end of every
                // note.
                if (change > 0.0)
                {
                    flux += change;
                }

                _previousBands[band] = bandStrengths[band];
            }

            return flux;
        }

        /// <summary>
        /// Works out what counts as a big jump, based on recent history.
        /// </summary>
        private double ComputeThreshold()
        {
            if (_historyCount == 0)
            {
                return double.MaxValue;
            }

            double total = 0.0;

            for (int i = 0; i < _historyCount; i++)
            {
                total += _history[i];
            }

            return (total / _historyCount) * Sensitivity;
        }

        /// <summary>
        /// Decides whether this reading is the start of a beat.
        ///
        /// Three things must all be true, and each rules out a different kind of
        /// false alarm.
        /// </summary>
        private bool IsOnset(double flux, double nowSeconds)
        {
            // There has to be something there at all. Near silence, the recent
            // average is almost zero and any flicker looks enormous next to it.
            if (flux < MinimumFlux)
            {
                return false;
            }

            // It has to be a bigger jump than this music has been making lately.
            if (flux < CurrentThreshold)
            {
                return false;
            }

            // It has to still be rising. A single hit spans several readings,
            // and without this the two or three readings after the peak would
            // each be reported as another beat.
            if (flux < _previousFlux)
            {
                return false;
            }

            // And enough time must have passed since the last one, so that the
            // shoulders of one drum hit are not counted as separate beats.
            return nowSeconds - _lastBeatSeconds >= MinimumSecondsBetweenBeats;
        }

        /// <summary>
        /// Stores a flux reading in the rolling history.
        /// </summary>
        private void RecordFlux(double flux)
        {
            _history[_historyPosition] = flux;
            _historyPosition = (_historyPosition + 1) % HistoryLength;

            if (_historyCount < HistoryLength)
            {
                _historyCount++;
            }
        }
    }
}
