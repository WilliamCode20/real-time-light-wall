using System;
using System.Collections.Generic;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Works out the tempo of the music from the timing of detected beats.
    ///
    /// HOW IT WORKS
    ///
    /// Beats arriving half a second apart mean 120 beats per minute. The whole
    /// idea is that simple; everything below exists to cope with the fact that
    /// detection is imperfect.
    ///
    /// Some beats get missed, which produces an interval twice as long as it
    /// should be. Some extra ones get detected, producing one half as long. So
    /// rather than trusting any single gap, this collects the recent ones and
    /// takes the middle value.
    ///
    /// The median is used rather than the average on purpose. One missed beat
    /// produces a doubled interval, and an average would be dragged upward by
    /// it; the median simply ignores it as an outlier. With imperfect detection
    /// that difference matters a great deal.
    ///
    /// THE OCTAVE PROBLEM
    ///
    /// Tempo is genuinely ambiguous, and not because of any flaw here. Music at
    /// 70 beats per minute and the same music counted at 140 are both correct
    /// descriptions - listeners disagree about this all the time, tapping along
    /// at half or double each other's rate.
    ///
    /// So intervals are folded by doubling or halving until they land in a
    /// sensible range for dance music. That means a slow track may be reported
    /// at twice its written tempo. That is not a bug, and for driving lights it
    /// barely matters: what counts is that the flashes line up with something
    /// the music is actually doing.
    /// </summary>
    public sealed class TempoEstimator
    {
        /// <summary>
        /// How many recent beats to remember. Around four bars of music, which
        /// is long enough to average out mistakes and short enough to follow a
        /// change of track.
        /// </summary>
        private const int MaximumBeatsRemembered = 17;

        /// <summary>
        /// How many intervals are needed before reporting anything.
        ///
        /// Below this, one bad detection would dominate and the reported tempo
        /// would jump around wildly - worse than admitting we do not know yet.
        /// </summary>
        private const int MinimumIntervalsNeeded = 4;

        /// <summary>
        /// How close an interval must be to the median to count as agreeing
        /// with it, as a fraction. Used to work out confidence.
        /// </summary>
        private const double AgreementTolerance = 0.12;

        /// <summary>When each remembered beat happened.</summary>
        private readonly List<double> _beatTimes = new();

        /// <summary>The gaps between them, reused to avoid allocating.</summary>
        private readonly List<double> _intervals = new();

        /// <summary>
        /// The slowest tempo reported. Anything slower is doubled until it fits.
        /// </summary>
        public double MinimumBpm { get; set; } = 70.0;

        /// <summary>
        /// The fastest tempo reported. Anything faster is halved until it fits.
        /// </summary>
        public double MaximumBpm { get; set; } = 180.0;

        /// <summary>
        /// The current estimate in beats per minute, or 0 when there is not yet
        /// enough to go on.
        /// </summary>
        public double Bpm { get; private set; }

        /// <summary>
        /// How consistent the recent beats have been, from 0 to 1.
        ///
        /// This is the fraction of recent gaps that agree with the estimate. A
        /// steady dance track gives something near 1; loose live playing or a
        /// passage with no clear beat gives much less.
        ///
        /// Worth showing in the interface. A confident wrong answer and an
        /// unconfident one look identical without it, and knowing which you have
        /// changes what to do about it.
        /// </summary>
        public double Confidence { get; private set; }

        /// <summary>
        /// Records that a beat happened.
        /// </summary>
        public void AddBeat(double timeSeconds)
        {
            _beatTimes.Add(timeSeconds);

            if (_beatTimes.Count > MaximumBeatsRemembered)
            {
                _beatTimes.RemoveAt(0);
            }

            Recalculate();
        }

        /// <summary>
        /// Forgets everything, for when playback stops or a new track starts.
        /// </summary>
        public void Reset()
        {
            _beatTimes.Clear();
            _intervals.Clear();

            Bpm = 0.0;
            Confidence = 0.0;
        }

        /// <summary>
        /// Drops the estimate when nothing has been heard for a while.
        ///
        /// Without this, the last tempo of a finished track would sit on screen
        /// indefinitely, which reads as a working estimate rather than a stale
        /// one.
        /// </summary>
        public void Update(double nowSeconds, double forgetAfterSeconds = 3.0)
        {
            if (_beatTimes.Count == 0)
            {
                return;
            }

            if (nowSeconds - _beatTimes[^1] > forgetAfterSeconds)
            {
                Reset();
            }
        }

        /// <summary>
        /// Reworks the estimate from the remembered beat times.
        /// </summary>
        private void Recalculate()
        {
            _intervals.Clear();

            for (int i = 1; i < _beatTimes.Count; i++)
            {
                double interval = _beatTimes[i] - _beatTimes[i - 1];

                if (interval <= 0.0)
                {
                    continue;
                }

                _intervals.Add(FoldIntoRange(interval));
            }

            if (_intervals.Count < MinimumIntervalsNeeded)
            {
                Bpm = 0.0;
                Confidence = 0.0;
                return;
            }

            _intervals.Sort();

            // The middle value. Robust against the doubled interval a missed
            // beat produces, which an average would be dragged upward by.
            double median = _intervals[_intervals.Count / 2];

            if (median <= 0.0)
            {
                Bpm = 0.0;
                Confidence = 0.0;
                return;
            }

            Bpm = 60.0 / median;

            // How many of the gaps agree with that. This is what separates
            // "the music has a clear steady beat" from "beats are being found
            // but they are all over the place".
            int agreeing = 0;

            foreach (double interval in _intervals)
            {
                if (Math.Abs(interval - median) <= median * AgreementTolerance)
                {
                    agreeing++;
                }
            }

            Confidence = (double)agreeing / _intervals.Count;
        }

        /// <summary>
        /// Doubles or halves an interval until it lands in the reportable range.
        ///
        /// This is what makes half-time and double-time detections agree with
        /// each other. If some beats are missed the gap doubles; folding brings
        /// it back alongside the correct ones so the median still finds the
        /// right answer instead of being split between two groups.
        /// </summary>
        private double FoldIntoRange(double intervalSeconds)
        {
            double shortest = 60.0 / MaximumBpm;
            double longest = 60.0 / MinimumBpm;

            // Guard against absurd values rather than looping forever on one.
            int safetyLimit = 8;

            while (intervalSeconds < shortest && safetyLimit-- > 0)
            {
                intervalSeconds *= 2.0;
            }

            while (intervalSeconds > longest && safetyLimit-- > 0)
            {
                intervalSeconds /= 2.0;
            }

            return intervalSeconds;
        }
    }
}
