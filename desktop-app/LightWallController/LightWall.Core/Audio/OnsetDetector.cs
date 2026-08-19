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

        /// <summary>
        /// How many readings are needed before the threshold means anything.
        ///
        /// The threshold now describes how much readings normally vary, and
        /// variation cannot be measured from two numbers. Eight is about a
        /// twelfth of a second, so nothing is lost at the start of a track.
        /// </summary>
        private const int MinimumHistoryToJudge = 8;

        /// <summary>Recent flux values, used to work out what is normal.</summary>
        private readonly double[] _history = new double[HistoryLength];

        /// <summary>
        /// Working space for sorting the history when finding a middle value.
        ///
        /// Kept as a field rather than made fresh each time because this runs on
        /// the audio thread about a hundred times a second, and that thread must
        /// not be given avoidable work - a new array per reading would be six
        /// thousand short-lived objects a minute for the garbage collector to
        /// deal with.
        /// </summary>
        private readonly double[] _sortingSpace = new double[HistoryLength];

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
        /// How far above the usual a jump has to be to count as a beat, measured
        /// in units of how much readings normally vary.
        ///
        /// Lower finds more beats but starts reporting ordinary texture as
        /// beats; higher only catches the most obvious hits and misses softer
        /// ones.
        ///
        /// THE UNITS CHANGED, AND SO DID THE NUMBER
        ///
        /// This used to be a multiplier on the average flux, and ran from about
        /// 1 to 3. It is now a multiple of the SPREAD added to the middle
        /// reading, which is a different quantity, so the old numbers mean
        /// nothing here - see ComputeThreshold for why the change was made.
        ///
        /// 5 was measured rather than guessed. Three synthetic tracks were
        /// played at the same tempo and loudness but with very different
        /// dynamics - near-silence between hits, moderate texture, and dense
        /// texture - and swept across settings from 0.5 to 6:
        ///
        ///   setting     sparse          moderate         dense
        ///     1.0     120 bpm 100%    119 bpm  32%    143 bpm  29%
        ///     3.0     120 bpm 100%    119 bpm  46%    119 bpm  42%
        ///     5.0     120 bpm 100%    120 bpm  94%    120 bpm  88%
        ///
        /// The point of the whole change is that bottom row: one setting that
        /// reads all three correctly. Under the old average-based threshold no
        /// such value existed, which is why the slider was being moved for
        /// nearly every song.
        ///
        /// Synthetic material only, though. Real music has structure that white
        /// noise does not, so this is a well-founded starting point rather than
        /// a settled answer - it still wants dialling in by ear.
        /// </summary>
        public double Sensitivity { get; set; } = 5.0;

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
        /// WHY 0.20 AND NOT SOMETHING SMALLER
        ///
        /// Covering the width of one hit only needs about 0.10. The original
        /// value was 0.12 on that reasoning, and it was too tight in practice.
        ///
        /// The extra is doing a second job: ignoring some of the sounds that are
        /// real but unhelpful. A drum hit is not the only thing that starts at a
        /// given moment - hi-hats, synth stabs and guitar chords all produce
        /// onsets, and most of them are not on the beat.
        ///
        /// 0.20 still allows up to 300 beats a minute, which is comfortably
        /// faster than the fastest tempo reported (180, or one beat every 0.33
        /// seconds), so no real beat is ever suppressed by it.
        ///
        /// A note on going further. Turning this up to 0.30 was tried by ear and
        /// did sound better, before the tempo estimator was rewritten - at 120
        /// beats a minute it suppresses the eighth notes, which used to send the
        /// tempo estimate wandering. That is no longer necessary: the estimator
        /// now copes with off-beat sounds directly rather than needing them
        /// hidden from it. 0.30 is also uncomfortably close to a real beat at
        /// the top of the tempo range, where beats are only 0.33 apart.
        ///
        /// The slider still reaches 0.30 if a particular room wants it.
        /// </summary>
        public double MinimumSecondsBetweenBeats { get; set; } = 0.20;

        /// <summary>
        /// How much of the flux must come from a genuine signal before anything
        /// is reported at all.
        ///
        /// Guards against near-silence, where the recent average is almost zero
        /// and any tiny flicker looks enormous by comparison.
        /// </summary>
        public double MinimumFlux { get; set; } = 0.01;

        /// <summary>
        /// Whether the detector should keep its own sensitivity in a workable
        /// range instead of leaving it to the person at the slider.
        ///
        /// WHAT IT AIMS AT, AND WHY NOT SOMETHING CLEVERER
        ///
        /// It does not try to find the "right" sensitivity, because there is no
        /// measurement of right available at this level - the detector cannot
        /// tell a beat from a well-timed guitar chord. What it can tell is
        /// whether it is finding a PLAUSIBLE NUMBER of things.
        ///
        /// Music runs from about 70 to 180 beats a minute, and real tracks carry
        /// off-beat sounds as well, so somewhere between roughly two and five
        /// detections a second is the healthy band. Far above it and ordinary
        /// texture is being reported as beats; far below and obvious hits are
        /// being missed. Either way the fix is the same knob in a known
        /// direction, which is what makes this safe to automate when hunting for
        /// the true tempo would not be.
        ///
        /// DELIBERATELY SLOW AND BOUNDED
        ///
        /// It judges over four seconds at a time and moves in small steps, so it
        /// drifts across a song rather than chasing a bar. It never leaves a
        /// sensible range. And it only judges when there is real audio to judge,
        /// so silence cannot walk it down to nothing and leave the next track
        /// triggering on everything.
        ///
        /// Off by default. Automatic behaviour that quietly disagrees with a
        /// slider somebody has just set is worse than no automatic behaviour.
        /// </summary>
        public bool AutoSensitivity { get; set; }

        /// <summary>How often the detection rate is judged, in seconds.</summary>
        private const double AutoWindowSeconds = 4.0;

        /// <summary>
        /// The healthy band of detections per second.
        ///
        /// The bottom is set by the slowest music worth following: 70 beats a
        /// minute is 1.17 a second, so anything at or below 1.0 means obvious
        /// hits are going missing rather than the music simply being slow.
        ///
        /// The top has to sit clear of a ceiling that is easy to miss.
        /// MinimumSecondsBetweenBeats caps the rate at five a second on its own,
        /// so a first attempt using 5.0 here could never fire - a detector
        /// triggering on absolutely everything sat exactly at the limit and was
        /// read as healthy. Measured: a dense track started far too loose stayed
        /// there and reported 77 BPM for a 120 BPM signal. 3.5 leaves room above
        /// the three a second that 180 BPM music produces while still being
        /// reachable.
        /// </summary>
        private const double AutoFewestPerSecond = 1.0;
        private const double AutoMostPerSecond = 3.5;

        /// <summary>
        /// How far sensitivity moves in one step, as a multiplier.
        ///
        /// Tightening is slightly brisker than loosening. Over-detection reads
        /// as noise and wants dealing with promptly; under-detection reads as
        /// restraint, so creeping down towards it is the safer direction to be
        /// slow in.
        /// </summary>
        private const double AutoTightenStep = 1.15;
        private const double AutoLoosenStep = 0.93;

        /// <summary>
        /// The range automatic adjustment will not leave.
        ///
        /// AutoHighest MUST MATCH the Beat size slider's Maximum in
        /// MainWindow.xaml. A slider stopping short of it does not merely fail
        /// to display the value: WPF clamps a Slider's Value to its Maximum, and
        /// the handler writes the slider back into this detector, so the
        /// interface would pull the setting back down every frame and cap
        /// automatic tightening below where it is allowed to reach.
        ///
        /// The slider's Minimum is deliberately lower than AutoLowest, which is
        /// fine and not the same kind of mismatch - automatic adjustment should
        /// not wander down there, but a person setting it by hand may.
        /// </summary>
        private const double AutoLowest = 1.5;
        private const double AutoHighest = 12.0;

        /// <summary>When the current judging window began.</summary>
        private double _autoWindowStartSeconds;

        /// <summary>How many beats have been reported in it.</summary>
        private int _detectionsThisWindow;

        /// <summary>The most recent flux value. Useful for diagnostics.</summary>
        public double CurrentFlux { get; private set; }

        /// <summary>The threshold the flux was measured against.</summary>
        public double CurrentThreshold { get; private set; }

        /// <summary>
        /// How close the last reading came to counting as a beat, where 1.0
        /// means "exactly on the line".
        ///
        /// WHY THIS EXISTS
        ///
        /// Sensitivity has to be set by ear, and the only feedback available
        /// otherwise is whether the wall flashed. That tells you a hit was
        /// missed but not by how much - and "missed by a hair" and "missed by
        /// miles" need opposite responses. One says nudge the slider, the other
        /// says the slider is the wrong thing to be touching.
        ///
        /// So this reports the size of the gap rather than just which side of it
        /// the reading landed on.
        ///
        /// WHAT IT IS MEASURED AGAINST
        ///
        /// A reading has to clear two separate bars: the moving threshold, and
        /// the minimum flux that stops near-silence triggering. Whichever is
        /// higher at the time is the one that actually matters, so that is what
        /// this compares against. Showing it against the threshold alone would
        /// read as 3.0 during silence, when in truth nothing was close to firing.
        ///
        /// AN IMPORTANT LIMIT
        ///
        /// Reaching 1.0 does NOT mean a beat was reported. Two of the four
        /// guards are about timing rather than size - the reading must still be
        /// rising, and enough time must have passed since the last beat - and
        /// neither is visible here. A run of readings sitting just above 1.0
        /// with no beats is the normal, correct look of a sustained note.
        /// </summary>
        public double TriggerRatio { get; private set; }

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
            TriggerRatio = ComputeTriggerRatio(flux, CurrentThreshold);

            bool isBeat = IsOnset(flux, nowSeconds);

            if (isBeat)
            {
                _lastBeatSeconds = nowSeconds;
            }

            RecordFlux(flux);
            _previousFlux = flux;

            if (isBeat)
            {
                _detectionsThisWindow++;
            }

            KeepSensitivityWorkable(nowSeconds);

            return isBeat;
        }

        /// <summary>
        /// Nudges the sensitivity when the detector is plainly finding too many
        /// things or too few. See AutoSensitivity.
        /// </summary>
        private void KeepSensitivityWorkable(double nowSeconds)
        {
            if (!AutoSensitivity)
            {
                // Keep the window rolling anyway, so switching this on does not
                // immediately act on a count gathered while it was off.
                _autoWindowStartSeconds = nowSeconds;
                _detectionsThisWindow = 0;
                return;
            }

            double elapsed = nowSeconds - _autoWindowStartSeconds;

            if (elapsed < AutoWindowSeconds)
            {
                return;
            }

            // Only judge when there is something to judge. Without this, silence
            // reads as "finding nothing" and would walk the sensitivity all the
            // way down, leaving the next track triggering on everything it hears.
            bool somethingIsPlaying =
                _historyCount >= MinimumHistoryToJudge && MiddleOfHistory() > MinimumFlux;

            if (somethingIsPlaying)
            {
                double perSecond = _detectionsThisWindow / elapsed;

                if (perSecond > AutoMostPerSecond)
                {
                    Sensitivity = Math.Min(Sensitivity * AutoTightenStep, AutoHighest);
                }
                else if (perSecond < AutoFewestPerSecond)
                {
                    Sensitivity = Math.Max(Sensitivity * AutoLoosenStep, AutoLowest);
                }
            }

            _autoWindowStartSeconds = nowSeconds;
            _detectionsThisWindow = 0;
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
            TriggerRatio = 0.0;

            // The automatic-adjustment window has to be forgotten too, and it
            // was originally missed here.
            //
            // Resetting happens when capture restarts, and AudioAnalyser.Reset
            // puts elapsed time back to zero at the same moment. A window start
            // left at, say, 300 seconds would then sit in the future: the
            // "have four seconds passed yet" test compares against a negative
            // elapsed and never fires, so automatic adjustment would lie dormant
            // for another five minutes with no sign anything was wrong.
            _autoWindowStartSeconds = 0.0;
            _detectionsThisWindow = 0;
        }

        /// <summary>
        /// Works out how close a reading came to being big enough to count.
        /// See TriggerRatio.
        /// </summary>
        private double ComputeTriggerRatio(double flux, double threshold)
        {
            // Whichever guard is higher is the one the reading actually has to
            // beat, so that is what to measure against.
            double barToClear = Math.Max(threshold, MinimumFlux);

            // Before enough history has built up, ComputeThreshold returns the
            // largest number there is, meaning "not ready to judge yet". Dividing
            // by it would give a ratio so small it displays as nothing, which is
            // the right answer, but it is clearer to say so outright.
            if (double.IsInfinity(barToClear) || barToClear >= double.MaxValue)
            {
                return 0.0;
            }

            // MinimumFlux is a fixed positive number, so barToClear cannot be
            // zero and this cannot divide by zero. Worth stating, because the
            // threshold on its own genuinely does reach zero during silence -
            // the history fills up with zeroes and their average is zero.
            if (barToClear <= 0.0)
            {
                return 0.0;
            }

            return flux / barToClear;
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
        ///
        /// TYPICAL, PLUS A MEASURE OF HOW MUCH THINGS NORMALLY VARY
        ///
        /// The threshold is the middle flux reading of recent history, plus
        /// Sensitivity times how far readings usually sit from that middle. So a
        /// beat is not "louder than average" but "further above the usual than
        /// readings usually get" - which is much closer to what a listener
        /// actually notices, and is the same question whatever the music is.
        ///
        /// WHAT THIS REPLACED, AND WHY IT NEEDED REPLACING
        ///
        /// It used to be the plain average of recent flux times Sensitivity.
        /// That worked, and it needed a different Sensitivity for almost every
        /// song - anywhere from about 1 to 2.5 - which is no use at all to
        /// somebody running a set.
        ///
        /// The reason is that an average is pulled about by the SHAPE of the
        /// distribution and not just its level, in two directions at once.
        ///
        /// On sparse material - acoustic drums, lots of space - the occasional
        /// huge flux spike drags the average up well above where ordinary
        /// readings sit, so the threshold ends up too high and softer hits are
        /// missed. The very hits being measured are what push the bar out of
        /// their own reach.
        ///
        /// On dense, heavily compressed material the opposite: readings are
        /// bunched close together, so the average sits right up among the peaks
        /// and almost nothing can clear a multiple of it.
        ///
        /// Both faults come from measuring the level and ignoring the spread.
        /// The middle value is not dragged about by a handful of large readings,
        /// and adding a share of the spread rather than multiplying the level is
        /// what makes one setting mean the same thing on both kinds of track.
        ///
        /// Note this is the same lesson TempoEstimator learned: prefer the
        /// middle value to the average whenever a few extreme readings are
        /// expected, because they are exactly what the answer should ignore.
        /// </summary>
        private double ComputeThreshold()
        {
            // Below a few readings there is no meaningful spread to measure, and
            // a threshold guessed from two numbers would let anything through.
            // The largest number there is means "not ready to judge yet".
            if (_historyCount < MinimumHistoryToJudge)
            {
                return double.MaxValue;
            }

            double typical = MiddleOfHistory();
            double spread = MiddleDistanceFrom(typical);

            return typical + (Sensitivity * spread);
        }

        /// <summary>
        /// The middle flux reading of recent history.
        /// </summary>
        private double MiddleOfHistory()
        {
            Array.Copy(_history, _sortingSpace, _historyCount);
            Array.Sort(_sortingSpace, 0, _historyCount);

            return _sortingSpace[_historyCount / 2];
        }

        /// <summary>
        /// How far a reading usually sits from the middle one.
        ///
        /// The middle of all the distances, rather than the average of them -
        /// for the same reason the middle is used above. A couple of enormous
        /// readings should not be allowed to widen what counts as normal
        /// variation, since those readings are the beats.
        /// </summary>
        private double MiddleDistanceFrom(double typical)
        {
            for (int i = 0; i < _historyCount; i++)
            {
                _sortingSpace[i] = Math.Abs(_history[i] - typical);
            }

            Array.Sort(_sortingSpace, 0, _historyCount);

            return _sortingSpace[_historyCount / 2];
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
