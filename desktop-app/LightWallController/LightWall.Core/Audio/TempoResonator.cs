using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Finds the tempo by asking how strongly the music pulses at each possible
    /// speed, rather than by measuring the gaps between detected beats.
    ///
    /// WHY THIS EXISTS ALONGSIDE TempoEstimator
    ///
    /// The two answer the same question from opposite ends, and the difference
    /// is what each one is allowed to see.
    ///
    /// TempoEstimator is given a list of TIMES. Before it ever runs, the onset
    /// curve has been compared against a threshold and flattened into events, so
    /// a thunderous beat and a barely-there one arrive identical, and a beat that
    /// fell a hair under the bar does not arrive at all. That projection cannot
    /// be undone downstream, and the machinery built to compensate for it is
    /// most of the tuning in this project - eleven coupled settings whose right
    /// values depend on the material and on each other.
    ///
    /// This is given the CURVE. Nothing is thresholded, nothing is discarded,
    /// and every reading contributes in proportion to how strong it actually
    /// was. It has two settings: how far back to listen, and which tempo to
    /// prefer when the music is genuinely ambiguous.
    ///
    /// HOW IT WORKS
    ///
    /// For each candidate tempo, keep one running total of the onset curve
    /// wound round a circle at that tempo's rate. Energy arriving in step with a
    /// candidate keeps landing at the same angle and piles up; energy out of step
    /// with it spreads round the circle and cancels. So the LENGTH of a
    /// candidate's total says how strongly the music pulses at that speed.
    ///
    /// The elegant part, and the reason this shape was chosen over a bank of
    /// comb filters: the DIRECTION of that same total says where the beats fall.
    /// Tempo and phase come out of one number and cannot disagree with each
    /// other - unlike a period estimate and a separately-nudged metronome, which
    /// are two guesses that can drift apart and have no way to correct one
    /// another.
    ///
    /// It also means the phase is MEASURED afresh from the signal on every
    /// reading rather than integrated forward from a period. An error in the
    /// tempo therefore cannot accumulate into a growing timing drift, which is
    /// exactly what a metronome running slightly fast does.
    ///
    /// WHAT IT DOES NOT SOLVE
    ///
    /// The octave question is genuine rather than a flaw here: music pulsing at
    /// 120 also pulses, truthfully, at 60 and at 240. Winding a pulse train
    /// round the circle at twice its rate lands every other pulse in the same
    /// place, so the double scores well too. Nothing in the signal settles this -
    /// listeners disagree about it constantly - so it is settled by preference
    /// instead. See PreferredBpm.
    /// </summary>
    public sealed class TempoResonator
    {
        /// <summary>The candidate tempos, in beats per minute.</summary>
        private double[] _candidateBpm = Array.Empty<double>();

        /// <summary>
        /// Each candidate's running total, wound round the circle at its own
        /// rate. Kept as two arrays rather than one of pairs, because this is
        /// walked on the audio thread and two flat runs of numbers are the
        /// friendliest thing to walk.
        /// </summary>
        private double[] _acrossCircle = Array.Empty<double>();
        private double[] _aroundCircle = Array.Empty<double>();

        /// <summary>
        /// A slow average of the onset curve, subtracted before winding.
        ///
        /// Without it, a passage that is simply LOUD would pile up in every
        /// candidate at once - the curve never returning to zero acts like a
        /// constant offset, and a constant is equally in step with everything.
        /// Subtracting it means only the RISES AND FALLS are wound, which is
        /// what carries the rhythm.
        /// </summary>
        private double _runningMean;

        /// <summary>When the last reading arrived.</summary>
        private double _lastSeconds;

        /// <summary>Whether anything has been fed in yet.</summary>
        private bool _started;

        /// <summary>How much has been gathered, for judging readiness.</summary>
        private double _gathered;

        /// <summary>
        /// The slowest and fastest tempo considered.
        ///
        /// Wider than TempoEstimator's 70 to 180, because this does not have to
        /// rely on the range to resolve the octave question - the preference
        /// below does that, and it does it gently rather than by refusing to
        /// consider an answer at all.
        /// </summary>
        public double MinimumBpm { get; init; } = 60.0;
        public double MaximumBpm { get; init; } = 200.0;

        /// <summary>
        /// How finely the range is divided.
        ///
        /// Half a beat per minute, which is about 0.4% at ordinary tempos. Fine
        /// enough that the phase stays in step with the music between readings,
        /// and there are only a few hundred candidates either way - each costing
        /// one multiply and one add per reading.
        /// </summary>
        public double BpmStep { get; init; } = 0.5;

        /// <summary>
        /// How long the running totals take to forget, in seconds.
        ///
        /// The one real setting. Long enough to span several bars so a single
        /// odd phrase cannot decide the answer, short enough to follow a track
        /// change. Everything about how quickly this responds and how firmly it
        /// holds on comes from this number, rather than from separate rules for
        /// each.
        /// </summary>
        public double MemorySeconds { get; set; } = 6.0;

        /// <summary>
        /// The tempo assumed most likely when the music is genuinely ambiguous.
        ///
        /// Music at 120 also pulses truthfully at 60 and 240, and no amount of
        /// listening settles which a person would tap - so something has to
        /// prefer. Around 120 is the standard choice and matches what listeners
        /// actually do.
        ///
        /// This replaces TempoEstimator's approach of refusing to consider
        /// anything outside 70 to 180, which settles the same question by
        /// pretending the other answers do not exist. A gentle preference is
        /// better behaved: a genuinely fast track can still be reported fast,
        /// it just has to be clearly the better reading.
        /// </summary>
        public double PreferredBpm { get; set; } = 120.0;

        /// <summary>
        /// How strong that preference is, as a spread in octaves.
        ///
        /// A little over half an octave, so doubling or halving is penalised
        /// noticeably but not fatally.
        /// </summary>
        public double PreferenceWidth { get; set; } = 0.55;

        /// <summary>
        /// How much has to have been heard before an answer is offered.
        ///
        /// Guards the opening moment, where one loud reading wound round the
        /// circle would be unanimous evidence for whatever it happened to align
        /// with.
        /// </summary>
        public double GatheredBeforeAnswering { get; set; } = 0.5;

        /// <summary>The tempo, in beats per minute, or 0 before there is one.</summary>
        public double Bpm { get; private set; }

        /// <summary>
        /// How far through the current beat, from 0 (just landed) to 1 (the next
        /// is due), read directly from the winning candidate's direction.
        /// </summary>
        public double BeatPhase { get; private set; }

        /// <summary>
        /// How much better the winner is than a genuinely different rival, from
        /// 0 to 1.
        ///
        /// Rivals within a few percent are the same answer counted twice and are
        /// skipped. The octave IS counted as a rival, so a track that pulses
        /// equally well at 85 and 170 honestly reports low confidence rather
        /// than picking one and sounding sure about it.
        /// </summary>
        public double Confidence { get; private set; }

        /// <summary>
        /// How strongly the music pulses at the winning tempo, before the
        /// preference is applied. Useful for telling "nothing rhythmic here"
        /// from "rhythmic but ambiguous".
        /// </summary>
        public double Strength { get; private set; }

        /// <summary>
        /// Feeds in one reading of the onset curve.
        /// </summary>
        /// <param name="onsetStrength">
        /// How much new energy just appeared. The raw curve - NOT thresholded,
        /// not smoothed, and not reduced to whether it counted as a beat. Every
        /// reading contributes in proportion to its size, which is the whole
        /// point of this class.
        /// </param>
        /// <param name="nowSeconds">The current time.</param>
        public void Update(double onsetStrength, double nowSeconds)
        {
            EnsureCandidates();

            if (!_started)
            {
                _started = true;
                _lastSeconds = nowSeconds;
                _runningMean = onsetStrength;
                return;
            }

            double elapsed = nowSeconds - _lastSeconds;
            _lastSeconds = nowSeconds;

            if (elapsed <= 0.0)
            {
                return;
            }

            // A stall - a debugger pause, a laptop waking - would otherwise wipe
            // every total in one step. Everything else that integrates over time
            // in this project caps its steps for the same reason.
            double safeElapsed = Math.Min(elapsed, 0.25);

            double keep = Math.Exp(-safeElapsed / MemorySeconds);

            _runningMean += (onsetStrength - _runningMean) * (1.0 - keep);
            _gathered = (_gathered * keep) + Math.Abs(onsetStrength);

            // Only what rises ABOVE the recent average is wound, and anything
            // below it counts as nothing rather than as a negative.
            //
            // WHY REJECTING THE FLOOR MATTERS, AND WHAT IT COST TO LEARN
            //
            // Allowing negatives seems more even-handed and was the first
            // version. Measured against real recordings it locked onto sparse
            // intros far more slowly than the thresholded path did - 36 seconds
            // against 10 on one track - and the reason is worth recording,
            // because it is the strongest argument FOR the design this class was
            // built to replace.
            //
            // A threshold does not only discard information. It also acts as a
            // noise gate, and on a sparse intro with a weak beat buried in
            // non-rhythmic texture, throwing away everything small is exactly
            // right: the small stuff is the least likely to be the beat, and
            // winding it round the circle spreads confusion into every candidate.
            //
            // Rectifying keeps the useful half of that. Content below the recent
            // average contributes nothing, which is the gating; content above it
            // contributes IN PROPORTION TO HOW FAR above, which is the part a
            // hard threshold throws away and this does not.
            double value = Math.Max(0.0, onsetStrength - _runningMean);

            for (int i = 0; i < _candidateBpm.Length; i++)
            {
                // Where this candidate's circle is pointing at this instant.
                double turns = _candidateBpm[i] * nowSeconds / 60.0;
                double angle = -2.0 * Math.PI * turns;

                _acrossCircle[i] = (_acrossCircle[i] * keep) + (value * Math.Cos(angle));
                _aroundCircle[i] = (_aroundCircle[i] * keep) + (value * Math.Sin(angle));
            }

            Decide(nowSeconds);
        }

        /// <summary>
        /// Forgets everything and starts listening afresh.
        /// </summary>
        public void Reset()
        {
            Array.Clear(_acrossCircle);
            Array.Clear(_aroundCircle);

            _runningMean = 0.0;
            _lastSeconds = 0.0;
            _started = false;
            _gathered = 0.0;

            Bpm = 0.0;
            BeatPhase = 0.0;
            Confidence = 0.0;
            Strength = 0.0;
        }

        /// <summary>
        /// Builds the candidate list, once.
        /// </summary>
        private void EnsureCandidates()
        {
            if (_candidateBpm.Length > 0)
            {
                return;
            }

            int count = (int)Math.Round((MaximumBpm - MinimumBpm) / BpmStep) + 1;

            _candidateBpm = new double[count];
            _acrossCircle = new double[count];
            _aroundCircle = new double[count];

            for (int i = 0; i < count; i++)
            {
                _candidateBpm[i] = MinimumBpm + (i * BpmStep);
            }
        }

        /// <summary>
        /// Picks the winner and reads the tempo, the phase and the confidence
        /// off it.
        /// </summary>
        private void Decide(double nowSeconds)
        {
            if (_gathered < GatheredBeforeAnswering)
            {
                Bpm = 0.0;
                Confidence = 0.0;
                Strength = 0.0;
                return;
            }

            int best = -1;
            double bestScore = 0.0;
            double bestLength = 0.0;

            for (int i = 0; i < _candidateBpm.Length; i++)
            {
                double length = Math.Sqrt(
                    (_acrossCircle[i] * _acrossCircle[i]) +
                    (_aroundCircle[i] * _aroundCircle[i]));

                double score = length * Preference(_candidateBpm[i]);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestLength = length;
                    best = i;
                }
            }

            if (best < 0)
            {
                Bpm = 0.0;
                Confidence = 0.0;
                Strength = 0.0;
                return;
            }

            // STICKING WITH THE CURRENT ANSWER UNLESS A RIVAL IS CLEARLY BETTER.
            //
            // Without this the reported tempo is whichever candidate happens to
            // be fractionally ahead at this instant, and when two are nearly
            // level that flickers between them several times a second. Measured
            // on a real recording it found the correct 126 after thirteen
            // seconds, left it again, and did not hold it steadily until
            // thirty-six - not because it could not find the answer, but because
            // it could not stay on it.
            //
            // This is the same trick BarHeightSmoother uses to stop the wall's
            // bars chattering between two heights, and for the same reason: a
            // boundary between two nearly-equal options is infinitely sharp, so
            // the answer has to depend slightly on which side you arrived from.
            //
            // It is the one piece of machinery here that exists to hold an
            // answer still rather than to find it, which is worth noting because
            // the design this class is measured against needs several.
            if (Bpm > 0.0)
            {
                int current = NearestCandidate(Bpm);

                if (current >= 0 && best != current)
                {
                    double currentScore = ScoreOf(current);

                    if (bestScore < currentScore * SwitchMargin)
                    {
                        best = current;
                        bestLength = LengthOf(current);
                        bestScore = currentScore;
                    }
                }
            }

            Bpm = _candidateBpm[best];
            Strength = bestLength;

            // The direction the winning total points says where the beats fall.
            //
            // Winding was done with a minus sign, so a pulse train starting at
            // t0 leaves the total pointing at -2*pi*f*t0. Undoing that gives the
            // position within the current beat directly, with no counting
            // forward from a start and therefore nothing that can drift.
            double angle = Math.Atan2(_aroundCircle[best], _acrossCircle[best]);
            double turns = (Bpm * nowSeconds / 60.0) + (angle / (2.0 * Math.PI));

            BeatPhase = turns - Math.Floor(turns);

            Confidence = MeasureConfidence(best, bestScore);
        }

        /// <summary>
        /// How much better a rival has to be before the answer moves to it.
        ///
        /// Fifteen percent. Large enough that two candidates jostling for the
        /// lead do not swap several times a second, small enough that a real
        /// change of tempo is followed within a beat or two.
        /// </summary>
        public double SwitchMargin { get; set; } = 1.15;

        /// <summary>The length of one candidate's running total.</summary>
        private double LengthOf(int index)
        {
            return Math.Sqrt(
                (_acrossCircle[index] * _acrossCircle[index]) +
                (_aroundCircle[index] * _aroundCircle[index]));
        }

        /// <summary>That length, with the tempo preference applied.</summary>
        private double ScoreOf(int index)
        {
            return LengthOf(index) * Preference(_candidateBpm[index]);
        }

        /// <summary>
        /// Which candidate a tempo corresponds to, or -1 if it is outside the
        /// range considered.
        /// </summary>
        private int NearestCandidate(double bpm)
        {
            int index = (int)Math.Round((bpm - MinimumBpm) / BpmStep);

            return index >= 0 && index < _candidateBpm.Length ? index : -1;
        }

        /// <summary>
        /// How much the winner beats the best genuinely different rival.
        ///
        /// This is about WHICH tempo rather than whether there is one. Fed pure
        /// noise it lands around 40%, because something wins by some margin
        /// purely by chance. Strength is the property that answers "is there any
        /// rhythm here at all"; there is a test making that split explicit.
        /// </summary>
        private double MeasureConfidence(int winner, double winningScore)
        {
            if (winningScore <= 0.0)
            {
                return 0.0;
            }

            double winnerBpm = _candidateBpm[winner];
            double bestRival = 0.0;

            for (int i = 0; i < _candidateBpm.Length; i++)
            {
                // Anything within a few percent is the same answer read twice,
                // not a rival. The octave deliberately IS a rival - a track that
                // pulses as well at half speed should not report certainty.
                if (Math.Abs(_candidateBpm[i] - winnerBpm) <= winnerBpm * 0.06)
                {
                    continue;
                }

                double length = Math.Sqrt(
                    (_acrossCircle[i] * _acrossCircle[i]) +
                    (_aroundCircle[i] * _aroundCircle[i]));

                double score = length * Preference(_candidateBpm[i]);

                if (score > bestRival)
                {
                    bestRival = score;
                }
            }

            return Math.Clamp(1.0 - (bestRival / winningScore), 0.0, 1.0);
        }

        /// <summary>
        /// How much this tempo is preferred, from 0 to 1, on the grounds of
        /// being the sort of speed people tap along at.
        ///
        /// Measured in octaves rather than in beats per minute, because the
        /// distance from 120 to 60 and from 120 to 240 should count the same -
        /// they are the same musical distance in opposite directions.
        /// </summary>
        private double Preference(double bpm)
        {
            double octaves = Math.Log(bpm / PreferredBpm) / Math.Log(2.0);

            return Math.Exp(-(octaves * octaves) / (2.0 * PreferenceWidth * PreferenceWidth));
        }
    }
}
