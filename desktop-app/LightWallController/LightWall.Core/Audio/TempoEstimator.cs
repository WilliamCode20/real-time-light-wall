using System;
using System.Collections.Generic;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Works out the tempo of the music from the timing of detected beats.
    ///
    /// HOW IT WORKS
    ///
    /// Rather than measuring gaps and hoping they average out, this tries every
    /// tempo in the reportable range and asks of each one: how much of what I
    /// have just heard would make sense at this speed?
    ///
    /// The test is that the distance between any two sounds should be a whole
    /// number of beats. At 120 beats a minute a beat is half a second, so two
    /// sounds 0.5, 1.0, 1.5 or 2.0 seconds apart all fit; two sounds 0.7 seconds
    /// apart do not. Every pair of recent sounds votes for the tempos it fits,
    /// and the tempo with the most votes wins.
    ///
    /// This is deliberately not clever. It is a lot of arithmetic on a small
    /// amount of data, and its virtue is that it cannot be derailed by any single
    /// bad reading the way a chain of measurements can.
    ///
    /// WHY IT WAS BUILT THIS WAY - THE VERSION BEFORE IT AND HOW IT FAILED
    ///
    /// The first version measured the gap between each sound and the one before
    /// it, doubled or halved each gap until it landed in the reportable range,
    /// and took the middle value. It worked well on clean material and fell over
    /// in exactly the place it was most needed.
    ///
    /// Two faults, both found by playing a real pop song and watching the
    /// confidence collapse when the chorus arrived.
    ///
    /// FAULT ONE: doubling a gap that is slightly wrong does not give a slightly
    /// wrong answer, it gives a confidently wrong one. A gap of 0.30 seconds is
    /// too short to be a beat, so it was doubled to 0.60 and reported as 100
    /// beats a minute. A gap of 0.20 became 0.40 and reported 150. Sounds landing
    /// a little off the grid - which is most of what a busy arrangement adds -
    /// did not blur the answer, they scattered it across the whole range.
    ///
    /// Measured on a simulated 120 BPM track with one extra sound per beat:
    /// landing exactly on the eighth note gave 120 BPM at 100% confidence, but
    /// moving that same sound 50 milliseconds later gave 100 BPM at 52%, and
    /// moving it to 0.40 seconds after the beat gave 150 BPM at 100% confidence -
    /// completely wrong and maximally sure of itself.
    ///
    /// FAULT TWO, and the deeper one: only neighbouring sounds were ever
    /// compared. Once every beat had a companion sound between it and the next,
    /// the real half-second spacing never appeared as a gap at all. The correct
    /// answer was not being outvoted - it was not on the ballot.
    ///
    /// Comparing every pair rather than only neighbours is what fixes the second
    /// fault, and scoring whole tempos rather than folding gaps one at a time is
    /// what fixes the first.
    ///
    /// A WRONG TURN WORTH RECORDING
    ///
    /// The obvious repair was to keep the median but widen it to include pairs
    /// further apart. Tried on the same simulated material, that did fix the
    /// tempo - 120 BPM in every case. But it dropped the reported confidence to
    /// around 50% even when the answer was exactly right, because many of those
    /// wider gaps are legitimately two or three beats long and did not match the
    /// one-beat median. It would have fixed the number and broken the thing that
    /// tells you whether to trust the number, which is worse than leaving it
    /// alone. Confidence had to be rethought as well, not just carried over.
    ///
    /// THE OCTAVE PROBLEM
    ///
    /// Tempo is genuinely ambiguous, and not because of any flaw here. Music at
    /// 70 beats per minute and the same music counted at 140 are both correct
    /// descriptions - listeners disagree about this all the time, tapping along
    /// at half or double each other's rate.
    ///
    /// Only tempos inside the reportable range are ever tried, so a very slow or
    /// very fast piece is reported at whatever multiple of its written tempo
    /// lands in that range. For driving lights that barely matters: what counts
    /// is that the flashes line up with something the music is actually doing.
    /// </summary>
    public sealed class TempoEstimator
    {
        /// <summary>
        /// How long a stretch of recent sounds to reason about, in seconds.
        ///
        /// A time window rather than a fixed number of sounds, which matters
        /// more than it looks. The old version remembered the last seventeen
        /// beats - so a busy passage producing three times as many detections
        /// shrank the window from about four bars to barely one, narrowing the
        /// evidence at exactly the moment it needed to be widest.
        /// </summary>
        public double WindowSeconds { get; set; } = 8.0;

        /// <summary>
        /// A hard ceiling on how many sounds are remembered.
        ///
        /// Every pair is compared, so the work grows with the square of this.
        /// The window above normally keeps the count well below it; this only
        /// exists so that a burst of false detections cannot make the arithmetic
        /// balloon on the audio thread.
        /// </summary>
        private const int MaximumOnsetsRemembered = 48;

        /// <summary>
        /// How many sounds are needed before reporting anything at all.
        ///
        /// Below this, one bad detection would dominate and the reported tempo
        /// would jump around wildly - worse than admitting we do not know yet.
        /// </summary>
        private const int MinimumOnsetsNeeded = 5;

        /// <summary>
        /// The furthest apart two sounds can be and still be compared, in
        /// seconds.
        ///
        /// Pairs further apart than this carry very little information: at a
        /// four second separation almost every tempo in the range can find some
        /// whole number of beats that nearly fits, so they vote for everything
        /// and distinguish nothing.
        /// </summary>
        private const double MaximumPairSeconds = 2.5;

        /// <summary>
        /// The most beats apart a pair is allowed to be.
        ///
        /// Same reasoning as above, expressed in beats rather than seconds -
        /// whichever limit bites first applies.
        /// </summary>
        private const int MaximumBeatsApart = 8;

        /// <summary>
        /// How far off a pair may sit and still count as fitting a tempo, as a
        /// fraction of one beat.
        ///
        /// A pair exactly on a beat boundary scores its full vote, one this far
        /// off scores nothing, and everything between scores in proportion. The
        /// sliding scale matters: a hard yes-or-no cutoff would make the winner
        /// flip back and forth between two tempos as a sound wandered a
        /// millisecond either side of the line.
        /// </summary>
        private const double FitTolerance = 0.12;

        /// <summary>
        /// How far apart two candidate tempos have to be before they count as
        /// genuinely different answers rather than the same answer twice.
        /// </summary>
        private const double DistinctTempoTolerance = 0.06;

        /// <summary>
        /// How much better a new tempo has to score than the settled one before
        /// it is treated as a serious challenger, when nothing is yet trusted.
        /// </summary>
        private const double ChallengeMarginWhenNew = 1.15;

        /// <summary>
        /// The same, once the settled tempo has earned full trust.
        /// </summary>
        private const double ChallengeMarginWhenTrusted = 1.5;

        /// <summary>
        /// How long a challenger must keep winning to overturn a tempo nothing
        /// is yet invested in, in seconds.
        /// </summary>
        private const double SecondsToOverturnWhenNew = 2.0;

        /// <summary>
        /// How long confident agreement takes to build full trust, in seconds.
        ///
        /// Roughly the length of a verse. Long enough that a tempo has to have
        /// genuinely settled, short enough to be established well before the
        /// first break arrives.
        /// </summary>
        private const double TrustBuildSeconds = 20.0;

        /// <summary>
        /// How long sustained disagreement takes to erode full trust, in
        /// seconds.
        ///
        /// THIS NUMBER IS WHAT MAKES TRACK CHANGES WORK.
        ///
        /// Trust that only ever accumulated would be a trap: a four minute song
        /// would build a position nothing could dislodge, and the next track
        /// would never get a look in.
        ///
        /// A break and a new song differ in exactly one way that can be measured
        /// - how LONG the disagreement lasts. A break runs ten to thirty seconds
        /// and then the drums come back. A new song disagrees forever. So trust
        /// erodes while the evidence is against it, and the consequence is the
        /// important part: how long a switch takes at a track boundary is set by
        /// this number, NOT by how long the previous song ran. A tempo held for
        /// four minutes and one held for forty seconds give way at the same
        /// pace once the evidence genuinely turns.
        ///
        /// Eight seconds is comfortably longer than a break but short enough
        /// that a new track is picked up within a few seconds - and faster than
        /// that when the change comes with a gap, since silence erodes trust too.
        /// </summary>
        private const double TrustDecaySeconds = 8.0;

        /// <summary>
        /// The largest step trust will take in one update, in seconds.
        ///
        /// A stall - a debugger pause, a laptop waking up - would otherwise
        /// arrive as one enormous gap and wipe out or fill up trust in a single
        /// step. Everything else in the project that integrates over time caps
        /// its steps for the same reason.
        /// </summary>
        private const double LargestTrustStepSeconds = 0.25;

        /// <summary>
        /// The simple ratios at which two readings are the same tempo counted
        /// differently rather than different tempos.
        ///
        /// Doubling and halving are the octave, and three-to-two either way is a
        /// triplet or a dotted feel. Nothing beyond these: the further the ratio
        /// gets from 1 the more genuinely different tempos happen to land near
        /// it, and absorbing one of those would be worse than the problem being
        /// solved.
        /// </summary>
        private static readonly double[] RelatedRatios =
        {
            0.5, 2.0 / 3.0, 1.5, 2.0
        };

        /// <summary>
        /// How much trust the settled tempo needs before a related reading is
        /// absorbed rather than treated as a challenge.
        ///
        /// WHY THIS IS GATED AT ALL
        ///
        /// Absorbing is right in the middle of a track, where a three-to-two
        /// reading means a section changed feel. It is wrong at the start of one,
        /// where the estimator may simply have picked the wrong reading first and
        /// needs to be free to correct itself - and wrong across a track change,
        /// where the next song genuinely might be half or one-and-a-half times
        /// the last.
        ///
        /// Trust already separates those cases: it is high when a tempo has been
        /// confirmed for a while, and low early on and after silence. Half a
        /// verse of agreement is about where absorbing starts being the safer
        /// answer.
        /// </summary>
        private const double TrustToAbsorbRelatedTempos = 0.5;

        /// <summary>
        /// How much longer a reading related by a simple ratio has to hold on
        /// before it takes over.
        ///
        /// At full trust the ordinary hold is eight seconds, so this makes it
        /// twenty-four - comfortably longer than a break that changes feel, and
        /// still short enough that a genuinely wrong multiple corrects itself
        /// inside half a minute rather than lasting the whole song.
        /// </summary>
        private const double RelatedReadingHoldMultiplier = 3.0;

        /// <summary>
        /// How well the settled tempo has to be doing before agreement counts
        /// towards trust.
        ///
        /// WHY TRUST HAS TO BE EARNED RATHER THAN JUST WAITED FOR
        ///
        /// Trust used to grow whenever nothing was actively beating the settled
        /// tempo, merely in proportion to confidence. A mediocre answer therefore
        /// still crept towards full trust given a long enough song, and then
        /// defended itself as though it had been right all along.
        ///
        /// A tempo that only half the sounds agree with has not earned the right
        /// to resist correction. Below this it holds its ground but stops
        /// gaining, so a poor answer stays as easy to shift as it was to adopt.
        /// </summary>
        private const double ConfidenceToEarnTrust = 0.5;

        /// <summary>How much the settled tempo has earned, from 0 to 1.</summary>
        private double _trust;

        /// <summary>When trust was last moved on.</summary>
        private double _lastTrustSeconds;

        /// <summary>
        /// Whether a rival is currently out-scoring the settled tempo by enough
        /// to be taken seriously.
        /// </summary>
        private bool _challengerLeading;

        /// <summary>
        /// How long a challenger has to keep winning to overturn a tempo that
        /// has earned FULL trust, in seconds.
        ///
        /// A tempo just adopted gives way in SecondsToOverturnWhenNew; one that
        /// has held confidently for a verse demands this instead, and everything
        /// between scales in proportion. See Trust.
        ///
        /// Note this is a ceiling rather than a fixed wait. Trust erodes while a
        /// challenger is leading, so the requirement is falling the whole time
        /// the challenge is being mounted - which is what stops a long-settled
        /// tempo becoming immovable.
        ///
        /// HOW MUCH THE HOLD IS ACTUALLY DOING - MEASURED, NOT ASSUMED
        ///
        /// Worth keeping from when this was a plain fixed wait. It was added
        /// expecting it to be what kept a busy chorus from dragging the tempo
        /// around. It is not.
        ///
        /// Running the messy-chorus test with the hold at zero - meaning any
        /// challenger takes over the instant it wins - still gives 120.5 BPM
        /// against a true 120. The scoring does that work on its own, because a
        /// chorus still contains the original beat and no rival ever gets far
        /// enough ahead to challenge in the first place.
        ///
        /// What the hold does do is cover the cases the scoring cannot settle: a
        /// section that moves to a half-time or double-time feel, or one where a
        /// triplet layer briefly outweighs the beat. Those are exactly the cases
        /// trust weighting is meant to strengthen.
        /// </summary>
        public double SecondsToOverturn { get; set; } = 8.0;

        /// <summary>
        /// How much the settled tempo has earned, from 0 to 1.
        ///
        /// Grows while beats keep confirming it and erodes while they do not.
        /// Exposed because it explains behaviour that otherwise looks arbitrary -
        /// why the same challenge is refused in one passage and accepted in
        /// another - and because tests need to be able to see it.
        /// </summary>
        public double Trust => _trust;

        /// <summary>When each remembered sound happened.</summary>
        private readonly List<double> _onsetTimes = new();

        /// <summary>The gaps between every pair, rebuilt on each recalculation.</summary>
        private readonly List<double> _pairGaps = new();

        /// <summary>The tempo currently trying to take over, or 0 for none.</summary>
        private double _challengerBpm;

        /// <summary>When that challenger first took the lead.</summary>
        private double _challengerSinceSeconds;

        /// <summary>The most recent time handed in, used for the challenge clock.</summary>
        private double _lastOnsetSeconds;

        /// <summary>
        /// The slowest tempo reported. Music slower than this is reported at a
        /// multiple of its true speed that falls inside the range.
        /// </summary>
        public double MinimumBpm { get; set; } = 70.0;

        /// <summary>
        /// The fastest tempo reported. Music faster than this is reported at a
        /// fraction of its true speed that falls inside the range.
        /// </summary>
        public double MaximumBpm { get; set; } = 180.0;

        /// <summary>
        /// The current estimate in beats per minute, or 0 when there is not yet
        /// enough to go on.
        /// </summary>
        public double Bpm { get; private set; }

        /// <summary>
        /// What share of the recent sounds actually land on the beat, from 0 to 1.
        ///
        /// WHAT THIS MEANS, AND WHAT IT REPLACED
        ///
        /// The old version reported the fraction of gaps agreeing with the
        /// median, which stopped meaning anything once pairs further apart were
        /// being considered - most of those legitimately span several beats.
        ///
        /// This asks a plainer question instead: having settled on a tempo, how
        /// much of what was heard sits on it? A clean four-to-the-floor track
        /// gives something near 1. A chorus with a syncopated synth layered over
        /// the same beat gives perhaps 0.5, which is honest - half the sounds
        /// genuinely are not on the beat, and the tempo underneath is still
        /// right.
        ///
        /// Worth showing in the interface. A confident wrong answer and an
        /// unconfident one look identical without it, and knowing which you have
        /// changes what to do about it.
        /// </summary>
        public double Confidence { get; private set; }

        /// <summary>
        /// The confidence worked out from the sounds themselves, before any
        /// fading for silence is applied.
        /// </summary>
        private double _measuredConfidence;

        /// <summary>
        /// How long a quiet stretch can last before the tempo is given up on.
        ///
        /// WHY THIS IS SO LONG
        ///
        /// Music goes quiet on purpose. A breakdown can run for eight bars with
        /// nothing but a pad, and at 120 beats a minute that is sixteen seconds
        /// with nothing for onset detection to find.
        ///
        /// An earlier version forgot the tempo after three seconds, which meant
        /// exactly those passages - the ones where holding the beat matters most
        /// - wiped the estimate and left the wall dead until the drums came back.
        ///
        /// Half a minute is longer than any breakdown and still short enough
        /// that a genuinely finished track does not leave a stale number sitting
        /// there looking current.
        /// </summary>
        public double ForgetAfterSeconds { get; set; } = 30.0;

        /// <summary>
        /// How long without beats before confidence starts falling.
        /// </summary>
        private const double ConfidenceHoldSeconds = 2.0;

        /// <summary>
        /// How long the fade from full confidence to none takes, once it starts.
        /// </summary>
        private const double ConfidenceFadeSeconds = 12.0;

        /// <summary>
        /// Records that a sound started.
        /// </summary>
        public void AddBeat(double timeSeconds)
        {
            _onsetTimes.Add(timeSeconds);
            _lastOnsetSeconds = timeSeconds;

            // Drop anything that has fallen out of the back of the window.
            double oldestKept = timeSeconds - WindowSeconds;

            while (_onsetTimes.Count > 0 && _onsetTimes[0] < oldestKept)
            {
                _onsetTimes.RemoveAt(0);
            }

            while (_onsetTimes.Count > MaximumOnsetsRemembered)
            {
                _onsetTimes.RemoveAt(0);
            }

            Recalculate();
        }

        /// <summary>
        /// Forgets everything, for when playback stops or a new track starts.
        /// </summary>
        public void Reset()
        {
            _onsetTimes.Clear();
            _pairGaps.Clear();

            Bpm = 0.0;
            Confidence = 0.0;
            _measuredConfidence = 0.0;
            _challengerBpm = 0.0;
            _challengerSinceSeconds = 0.0;
            _lastOnsetSeconds = 0.0;
            _trust = 0.0;
            _lastTrustSeconds = 0.0;
            _challengerLeading = false;
        }

        /// <summary>
        /// Keeps the estimate alive through quiet passages, and eventually
        /// retires it.
        ///
        /// The tempo itself is HELD rather than dropped. A quiet section does
        /// not mean the music changed speed - it means there is nothing to
        /// measure - and the right answer is still the last one we worked out.
        ///
        /// Confidence falls instead. That way anything reading these values can
        /// tell the difference between "120, measured just now" and "still 120,
        /// but nothing has confirmed it for a while", which is exactly the
        /// distinction worth knowing during a breakdown.
        /// </summary>
        public void Update(double nowSeconds)
        {
            if (_onsetTimes.Count == 0)
            {
                _trust = 0.0;
                _lastTrustSeconds = nowSeconds;
                return;
            }

            double quietFor = nowSeconds - _onsetTimes[^1];

            // Before the early exits below, so trust keeps moving in every
            // passage rather than only in the ones that reach the end of this
            // method.
            UpdateTrust(nowSeconds, quietFor);

            if (quietFor > ForgetAfterSeconds)
            {
                Reset();
                return;
            }

            if (quietFor <= ConfidenceHoldSeconds)
            {
                Confidence = _measuredConfidence;
                return;
            }

            // Fade confidence while keeping the tempo. The pulse carries on at
            // the last known speed, which is what lets a breakdown stay in time.
            double fade = 1.0 - ((quietFor - ConfidenceHoldSeconds) / ConfidenceFadeSeconds);

            Confidence = _measuredConfidence * Math.Clamp(fade, 0.0, 1.0);
        }

        /// <summary>
        /// Moves trust in whichever direction the evidence points.
        ///
        /// Trust grows while beats keep confirming the settled tempo, and erodes
        /// while they do not - either because a rival is out-scoring it, or
        /// because there is nothing being heard at all.
        ///
        /// WHY SILENCE COUNTS AGAINST IT
        ///
        /// This looks harsh on a quiet break, and costs nothing there. A quiet
        /// passage produces no challenger, so there is nobody to hand over to
        /// however low trust falls - and the tempo itself is held for thirty
        /// seconds regardless. When the drums come back at the same speed, trust
        /// simply rebuilds.
        ///
        /// What it buys is track changes. Most transitions carry a gap or a hard
        /// cut, and a couple of seconds of silence knocking trust down means the
        /// next track is picked up quickly instead of fighting the last one.
        ///
        /// Growth is scaled by confidence, so a tempo agreed on by nearly every
        /// recent sound earns its position faster than one scraping along on
        /// half of them. Erosion is not scaled: once the evidence has turned,
        /// how good the answer used to look stops being the point.
        /// </summary>
        private void UpdateTrust(double nowSeconds, double quietFor)
        {
            double elapsed = Math.Min(nowSeconds - _lastTrustSeconds, LargestTrustStepSeconds);
            _lastTrustSeconds = nowSeconds;

            if (elapsed <= 0.0)
            {
                return;
            }

            if (Bpm <= 0.0)
            {
                _trust = 0.0;
                return;
            }

            bool losingGround = _challengerLeading || quietFor > ConfidenceHoldSeconds;

            if (losingGround)
            {
                _trust -= elapsed / TrustDecaySeconds;
            }
            else if (_measuredConfidence >= ConfidenceToEarnTrust)
            {
                _trust += elapsed * _measuredConfidence / TrustBuildSeconds;
            }

            // Note the missing third case: an answer that nothing is beating but
            // that only half the sounds agree with neither gains nor loses. It
            // keeps whatever it has already earned and stops climbing, so a
            // mediocre reading cannot reach full trust just by lasting a while.
            _trust = Math.Clamp(_trust, 0.0, 1.0);
        }

        /// <summary>
        /// Reworks the estimate from the remembered sounds.
        /// </summary>
        private void Recalculate()
        {
            if (_onsetTimes.Count < MinimumOnsetsNeeded)
            {
                // A THIN PATCH IS NOT A REASON TO THROW AWAY A GOOD ANSWER.
                //
                // This used to set Bpm to zero, which quietly defeated the whole
                // hold-through-quiet-passages design from the other direction.
                // The hold in Update is written to keep the tempo alive for
                // thirty seconds and fade only confidence - but a break sparse
                // enough to leave fewer than a handful of sounds in the window
                // came through here first and wiped the tempo outright, long
                // before the hold ever got a say.
                //
                // Measured on a break with a sound every two seconds: the tempo
                // went to zero, which stops Tempo Pulse dead rather than letting
                // it carry the beat through - exactly the passage it exists for.
                //
                // Nothing established yet still reports nothing, because there
                // genuinely is no answer to give.
                if (Bpm <= 0.0)
                {
                    Confidence = 0.0;
                    _measuredConfidence = 0.0;
                }

                return;
            }

            BuildPairGaps();

            if (_pairGaps.Count == 0)
            {
                return;
            }

            // Try every whole tempo in the range and keep the best two. The
            // runner-up is kept only so that a genuinely different tempo scoring
            // nearly as well can be spotted.
            double bestBpm = 0.0;
            double bestScore = 0.0;

            int slowest = (int)Math.Floor(MinimumBpm);
            int fastest = (int)Math.Ceiling(MaximumBpm);

            for (int bpm = slowest; bpm <= fastest; bpm++)
            {
                double score = ScoreTempo(60.0 / bpm);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestBpm = bpm;
                }
            }

            if (bestBpm <= 0.0)
            {
                Bpm = 0.0;
                Confidence = 0.0;
                _measuredConfidence = 0.0;
                return;
            }

            // Whole numbers of BPM are too coarse on their own - 174 BPM and 175
            // are only a millisecond apart per beat, but over four beats that
            // adds up. So the winner is refined against the pairs that actually
            // support it, which pins it down far more precisely than the step
            // size of the search.
            double refinedBpm = RefineTempo(60.0 / bestBpm);

            ApplyWithInertia(refinedBpm);

            _measuredConfidence = MeasureCoverage(60.0 / Bpm);
            Confidence = _measuredConfidence;
        }

        /// <summary>
        /// Collects the distance between every pair of remembered sounds that is
        /// close enough together to be informative.
        /// </summary>
        private void BuildPairGaps()
        {
            _pairGaps.Clear();

            for (int first = 0; first < _onsetTimes.Count; first++)
            {
                for (int second = first + 1; second < _onsetTimes.Count; second++)
                {
                    double gap = _onsetTimes[second] - _onsetTimes[first];

                    // The list is in time order, so once one pair is too far
                    // apart every later one will be too.
                    if (gap > MaximumPairSeconds)
                    {
                        break;
                    }

                    if (gap > 0.0)
                    {
                        _pairGaps.Add(gap);
                    }
                }
            }
        }

        /// <summary>
        /// Asks how much of what was heard would make sense at a given tempo.
        ///
        /// Each pair of sounds votes if the distance between them is close to a
        /// whole number of beats at this tempo. The vote is worth less the more
        /// beats apart the pair is, because two sounds one beat apart are much
        /// stronger evidence than two sounds five beats apart - at five beats,
        /// almost any tempo can find some multiple that nearly fits.
        ///
        /// That weighting is also what settles the choice between a tempo and
        /// double it. Both explain the same sounds, but the slower one explains
        /// them at lower multiples, so it scores higher and wins.
        /// </summary>
        private double ScoreTempo(double periodSeconds)
        {
            double total = 0.0;
            double allowedError = periodSeconds * FitTolerance;

            foreach (double gap in _pairGaps)
            {
                double beatsApart = gap / periodSeconds;
                int nearestWholeBeat = (int)Math.Round(beatsApart);

                if (nearestWholeBeat < 1 || nearestWholeBeat > MaximumBeatsApart)
                {
                    continue;
                }

                double error = Math.Abs(gap - (nearestWholeBeat * periodSeconds));

                if (error >= allowedError)
                {
                    continue;
                }

                // Full marks for landing exactly on a beat, sliding to nothing
                // at the edge of what counts as fitting.
                double closeness = 1.0 - (error / allowedError);

                total += closeness / nearestWholeBeat;
            }

            return total;
        }

        /// <summary>
        /// Sharpens a tempo by averaging the pairs that support it.
        ///
        /// Each supporting pair implies a beat length of its own - a pair three
        /// beats apart implies a beat a third of that distance. Averaging those
        /// gives a far more precise answer than the whole-BPM steps the search
        /// used, and costs one more pass over the same data.
        /// </summary>
        private double RefineTempo(double periodSeconds)
        {
            double totalImplied = 0.0;
            double totalWeight = 0.0;
            double allowedError = periodSeconds * FitTolerance;

            foreach (double gap in _pairGaps)
            {
                int nearestWholeBeat = (int)Math.Round(gap / periodSeconds);

                if (nearestWholeBeat < 1 || nearestWholeBeat > MaximumBeatsApart)
                {
                    continue;
                }

                double error = Math.Abs(gap - (nearestWholeBeat * periodSeconds));

                if (error >= allowedError)
                {
                    continue;
                }

                double weight = (1.0 - (error / allowedError)) / nearestWholeBeat;

                totalImplied += (gap / nearestWholeBeat) * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0.0)
            {
                return 60.0 / periodSeconds;
            }

            return 60.0 / (totalImplied / totalWeight);
        }

        /// <summary>
        /// Decides whether to take a newly measured tempo or stay where we are.
        ///
        /// Music does not change speed part way through, so once an estimate has
        /// settled it should take real persuading to move. Without that, a busy
        /// chorus can shove the reading around even when the beat underneath
        /// never changed.
        ///
        /// But it must not be permanent either. The next track will be at a
        /// different speed and nothing here knows where one song ends and the
        /// next begins. So a rival has to win by a clear margin AND keep winning
        /// for several seconds before it takes over - long enough that a chorus
        /// cannot do it, short enough that a new song is picked up quickly.
        /// </summary>
        private void ApplyWithInertia(double measuredBpm)
        {
            // Nothing settled yet, so there is nothing to be loyal to.
            if (Bpm <= 0.0)
            {
                Bpm = measuredBpm;
                _challengerBpm = 0.0;
                _challengerLeading = false;
                return;
            }

            bool sameAsBefore =
                Math.Abs(measuredBpm - Bpm) <= Bpm * DistinctTempoTolerance;

            if (sameAsBefore)
            {
                // Track it gently rather than snapping, so the reading settles
                // instead of twitching by a fraction of a BPM every beat.
                Bpm += (measuredBpm - Bpm) * 0.25;
                _challengerBpm = 0.0;
                _challengerLeading = false;
                return;
            }

            // THE SAME TEMPO COUNTED DIFFERENTLY IS NOT A RIVAL.
            //
            // A section that moves to a triplet or a dotted feel measures as
            // one and a half times the tempo, or two thirds of it, without the
            // music having changed speed at all. Treating that as a challenger
            // and switching to it is both wrong and highly visible.
            //
            // Measured on a settled 120: a break of triplets dragged the
            // estimate to 180 and a dotted one dragged it to 80 - each the
            // same tempo wearing a different hat.
            //
            // Doubling and halving are deliberately in the list too, though they
            // turned out already to be handled: only tempos between 70 and 180
            // are ever tried, and the scoring prefers whichever explains the
            // sounds at lower multiples. Half-time and double-time breaks both
            // measured a rock-steady 120 before this was written. They are
            // included because relying on the range to keep covering that is
            // luck rather than design, and the cost of listing them is nothing.
            // Is this reading the same tempo counted differently?
            bool relatedReading = false;

            if (_trust >= TrustToAbsorbRelatedTempos)
            {
                double folded = FoldTowards(measuredBpm, Bpm);

                if (Math.Abs(folded - Bpm) <= Bpm * DistinctTempoTolerance)
                {
                    relatedReading = true;

                    // Keep refining the settled reading from it, since a related
                    // reading still carries information about the true speed.
                    Bpm += (folded - Bpm) * 0.25;
                }
            }

            // Something different is being measured. Is it beating the settled
            // answer by enough to be taken seriously?
            //
            // HOW MUCH IS ENOUGH DEPENDS ON WHAT THE SETTLED ANSWER HAS EARNED
            //
            // A tempo adopted four seconds ago and one that has held confidently
            // for a whole verse used to be defended identically, which is why a
            // break could shove the estimate around so easily. Both the margin a
            // challenger must clear and the time it must hold now scale with
            // trust.
            //
            // The ceiling matters as much as the scaling. Trust erodes while a
            // challenger leads, so these requirements are falling the whole time
            // a challenge is being mounted - a long-settled tempo is harder to
            // shift, never impossible. That is what keeps track changes working.
            double margin = ChallengeMarginWhenNew +
                (_trust * (ChallengeMarginWhenTrusted - ChallengeMarginWhenNew));

            double settledScore = ScoreTempo(60.0 / Bpm);
            double measuredScore = ScoreTempo(60.0 / measuredBpm);

            if (measuredScore < settledScore * margin)
            {
                // Not convincing. Stay put and forget it.
                _challengerBpm = 0.0;
                _challengerLeading = false;
                return;
            }

            // Something credible is leading, so the settled tempo starts losing
            // ground whether or not this particular challenger ever wins.
            //
            // A related reading is the exception. It is usually a section that
            // changed feel rather than evidence of anything being wrong, so it
            // must not erode trust - otherwise a long break would grind the
            // settled tempo's defences down and flip it after all.
            _challengerLeading = !relatedReading;

            bool sameChallengerAsLastTime =
                _challengerBpm > 0.0 &&
                Math.Abs(measuredBpm - _challengerBpm) <= _challengerBpm * DistinctTempoTolerance;

            if (!sameChallengerAsLastTime)
            {
                // A new contender. Start its clock.
                _challengerBpm = measuredBpm;
                _challengerSinceSeconds = _lastOnsetSeconds;
                return;
            }

            // How long this challenger has to hold on, scaled by what it is up
            // against. Recomputed every time rather than fixed when the
            // challenge started, so the falling trust shortens the wait as the
            // challenge goes on.
            double requiredHold = SecondsToOverturnWhenNew +
                (_trust * (SecondsToOverturn - SecondsToOverturnWhenNew));

            // A RELATED READING HAS TO WORK MUCH HARDER, BUT NOT FOREVER.
            //
            // The first attempt at this absorbed related readings outright and
            // returned. That was a trap: if the estimator latched onto 180 for
            // music that is really 120, every later reading of 120 was folded
            // back onto 180 and swallowed as agreement, so the wrong answer
            // defended itself permanently and grew more trusted the longer it
            // was wrong. Reported from listening as "it got the wrong BPM and
            // just held onto it".
            //
            // The second attempt let a related reading correct the settled one
            // as soon as it explained the music better. That failed the other
            // way, and the break tests caught it immediately: during a triplet
            // section the 180 reading genuinely DOES explain the sounds better,
            // so it corrected straight to 180 - exactly what absorbing was
            // meant to prevent.
            //
            // What separates the two cases is not which fits better. It is how
            // long it lasts. A section that changed feel ends; a wrong multiple
            // does not. So a related reading is a challenger like any other,
            // just one that has to hold on for far longer - long enough that no
            // realistic break outlasts it, short enough that a genuinely wrong
            // multiple still puts itself right without help.
            if (relatedReading)
            {
                requiredHold *= RelatedReadingHoldMultiplier;
            }

            if (_lastOnsetSeconds - _challengerSinceSeconds >= requiredHold)
            {
                Bpm = measuredBpm;
                _challengerBpm = 0.0;
                _challengerLeading = false;

                // A tempo just adopted has earned nothing yet, and must prove
                // itself the same way the last one did.
                _trust = 0.0;
            }
        }

        /// <summary>
        /// Reads a tempo at whichever simple ratio lands nearest the settled
        /// one, so that the same tempo counted differently comes out the same.
        ///
        /// Returns the reading unchanged when no ratio gets closer, which is
        /// what keeps a genuinely different tempo recognisable as a challenger.
        /// </summary>
        private static double FoldTowards(double bpm, double settled)
        {
            double best = bpm;
            double bestDistance = Math.Abs(bpm - settled);

            foreach (double ratio in RelatedRatios)
            {
                double candidate = bpm * ratio;
                double distance = Math.Abs(candidate - settled);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Works out what share of the remembered sounds actually land on the
        /// beat at a given tempo. See Confidence.
        ///
        /// The beat has to be located before anything can be counted, since a
        /// tempo says how far apart the beats are but not where they fall. So
        /// each sound is placed by how far through a beat it sits, the busiest
        /// such position is taken to be the beat itself, and the sounds near it
        /// are counted.
        /// </summary>
        private double MeasureCoverage(double periodSeconds)
        {
            if (_onsetTimes.Count == 0 || periodSeconds <= 0.0)
            {
                return 0.0;
            }

            int bestCount = 0;

            // Try each sound in turn as the one sitting on the beat, and see how
            // many others agree with it. Simpler than averaging positions, and
            // it does not fall apart when the sounds form two clusters rather
            // than one - which is precisely what a beat plus an off-beat synth
            // produces.
            foreach (double candidate in _onsetTimes)
            {
                int count = 0;

                foreach (double onset in _onsetTimes)
                {
                    double distance = Math.Abs(onset - candidate);
                    double intoBeat = distance % periodSeconds;

                    // A sound just before a beat is as close to it as one just
                    // after, so the shorter way round is what counts.
                    double offBy = Math.Min(intoBeat, periodSeconds - intoBeat);

                    if (offBy <= periodSeconds * FitTolerance)
                    {
                        count++;
                    }
                }

                if (count > bestCount)
                {
                    bestCount = count;
                }
            }

            return (double)bestCount / _onsetTimes.Count;
        }
    }
}
