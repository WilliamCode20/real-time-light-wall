using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Writes down what the analyser saw, reading by reading, so that a real
    /// piece of music can be studied afterwards instead of only watched live.
    ///
    /// WHY THIS EXISTS
    ///
    /// Beat detection is the one part of this project still judged by ear. That
    /// is a real limitation: listening tells you THAT a break went wrong, but
    /// not which of half a dozen possible reasons caused it, and it cannot be
    /// replayed identically while a fix is tried.
    ///
    /// Worse, it resists the method the rest of the project relies on. Synthetic
    /// signals can be built to order and their answers are known in advance, but
    /// they are far cleaner than real music - a synthetic break made of decaying
    /// sine bursts is handled perfectly by the detector as it stands, while real
    /// breaks are reported as a problem. When the test material refuses to
    /// reproduce the fault, the test material is what needs replacing.
    ///
    /// WHAT MAKES THIS A COMPLETE RECORD, NOT A SUMMARY
    ///
    /// The important property, and the reason this is worth building rather than
    /// guessing: the beat detector's ENTIRE input is the seven raw band
    /// strengths and a timestamp. OnsetDetector.Update takes nothing else, and
    /// TempoEstimator downstream of it consumes nothing but the times of the
    /// onsets that came out.
    ///
    /// So a log of raw band strengths against time is not a partial view of what
    /// happened - it is everything the detection chain had to work with. A
    /// recording can therefore be replayed through a DIFFERENT detector and give
    /// exactly what that detector would have done on that music, rather than an
    /// approximation of it. That is what turns "this break sounds wrong" into an
    /// experiment that can be run as many times as it takes.
    ///
    /// Everything else recorded here - flux, threshold, tempo, trust - is
    /// derived from those seven numbers and is stored anyway, so that a replay
    /// can be checked against what actually happened at the time. If a replay
    /// disagrees with the recorded flux, the replay is wrong.
    ///
    /// WHAT IT DELIBERATELY DOES NOT CAPTURE
    ///
    /// The audio itself. Nothing here can be turned back into sound, which keeps
    /// a recording something that can be handed around freely. It also means a
    /// fault living UPSTREAM of the band split - in the transform, or in where
    /// the band boundaries were drawn - would show up here as a symptom without
    /// its cause. Worth remembering before concluding too much from a trace.
    ///
    /// THREADS
    ///
    /// Written from two of them, which is not obvious and is why this takes a
    /// lock. Process runs on the audio thread, where Windows delivers buffers.
    /// ProcessSilence runs on the INTERFACE thread, from the window's redraw
    /// loop, because silence arrives as an absence of buffers and has to be
    /// noticed rather than received. Both record.
    ///
    /// The lock is safe on the audio thread because it is never contended in
    /// practice: the two writers between them produce a few hundred readings a
    /// second and each holds the lock for the time it takes to copy about thirty
    /// numbers. Taking an uncontended lock costs a few tens of nanoseconds.
    ///
    /// NO ALLOCATION WHILE RECORDING
    ///
    /// Readings go into one flat array of numbers, laid out end to end, which is
    /// allocated once when recording starts. Nothing is created per reading.
    ///
    /// That follows the same rule as the rest of the audio path - see the note
    /// on OnsetDetector's sorting space - and matters for the same reason: the
    /// audio thread must not be handed avoidable work, and a fresh object a
    /// hundred times a second is a steady stream of rubbish for the garbage
    /// collector whose cleanup pauses show up as glitches.
    /// </summary>
    public sealed class AnalysisRecorder
    {
        /// <summary>
        /// How many numbers each reading occupies in the flat array.
        ///
        /// Laying the readings end to end in one array rather than making an
        /// object per reading is what keeps recording allocation-free. Reading
        /// number N starts at position N * ValuesPerReading.
        ///
        /// This MUST match both ColumnHeadings and the order values are written
        /// in Record. All three are changed together or none of them.
        /// </summary>
        private const int ValuesPerReading = 28;

        /// <summary>
        /// How long a recording may run before it stops accepting readings, in
        /// seconds.
        ///
        /// Ten minutes is far longer than the passage worth studying - a break
        /// is twenty seconds - and the limit exists only so that a session left
        /// recording overnight cannot eat memory without bound.
        /// </summary>
        private const double MaximumSecondsRecorded = 600.0;

        /// <summary>
        /// How many readings a second to make room for.
        ///
        /// Buffers arrive around a hundred times a second and the silence check
        /// adds a few more, so 150 leaves comfortable margin. Being generous
        /// here costs memory and nothing else; being mean would end a recording
        /// early.
        /// </summary>
        private const int AssumedReadingsPerSecond = 150;

        /// <summary>Guards everything below. See the note on threads.</summary>
        private readonly object _gate = new();

        /// <summary>
        /// Every reading, laid end to end. Null until recording starts.
        /// </summary>
        private double[]? _values;

        /// <summary>How many readings have been stored.</summary>
        private int _readingCount;

        /// <summary>
        /// Set to 1 when the user asks for a mark, cleared by the next reading.
        ///
        /// Written by whichever thread the button press arrives on and read by
        /// the recording threads, so it is moved with Interlocked - an operation
        /// that cannot be interrupted half way. A plain bool would work in
        /// practice but this says the intent outright.
        /// </summary>
        private int _markPending;

        /// <summary>True while readings are being stored.</summary>
        public bool IsRecording { get; private set; }

        /// <summary>How many readings have been stored so far.</summary>
        public int ReadingCount
        {
            get { lock (_gate) { return _readingCount; } }
        }

        /// <summary>
        /// True when recording stopped because it ran out of room rather than
        /// because it was asked to.
        /// </summary>
        public bool ReachedLimit { get; private set; }

        /// <summary>
        /// How many marks the user has dropped, so the interface can confirm a
        /// press landed.
        /// </summary>
        public int MarkCount { get; private set; }

        /// <summary>
        /// The seconds covered by the recording so far, taken from the first and
        /// last readings rather than from a clock.
        /// </summary>
        public double SecondsRecorded
        {
            get
            {
                lock (_gate)
                {
                    if (_values is null || _readingCount < 2)
                    {
                        return 0.0;
                    }

                    double first = _values[0];
                    double last = _values[(_readingCount - 1) * ValuesPerReading];

                    return last - first;
                }
            }
        }

        /// <summary>
        /// Clears anything held and begins a new recording.
        /// </summary>
        public void Start()
        {
            lock (_gate)
            {
                int capacity = (int)(MaximumSecondsRecorded * AssumedReadingsPerSecond);

                // Allocated here rather than when this object is created, so an
                // app that never records never pays for the room.
                _values = new double[capacity * ValuesPerReading];

                _readingCount = 0;
                _markPending = 0;
                MarkCount = 0;
                ReachedLimit = false;
                IsRecording = true;
            }
        }

        /// <summary>
        /// Stops recording. What was gathered is kept, ready to be written out.
        /// </summary>
        public void Stop()
        {
            lock (_gate)
            {
                IsRecording = false;
            }
        }

        /// <summary>
        /// Releases the recording from memory.
        /// </summary>
        public void Clear()
        {
            lock (_gate)
            {
                IsRecording = false;
                _values = null;
                _readingCount = 0;
                MarkCount = 0;
                ReachedLimit = false;
            }
        }

        /// <summary>
        /// Flags the next reading, so an interesting moment can be found later.
        ///
        /// Pressed by a person while the music plays - as a break starts, say.
        /// Without it, finding the passage that mattered means searching a file
        /// of tens of thousands of readings for something that only sounded
        /// wrong.
        ///
        /// The mark lands on the NEXT reading rather than being timestamped
        /// here, which keeps it exactly aligned to the data instead of sitting
        /// between two rows. A human pressing a button is already a good
        /// fraction of a second late, so the extra hundredth costs nothing.
        /// </summary>
        public void Mark()
        {
            if (!IsRecording)
            {
                return;
            }

            Interlocked.Exchange(ref _markPending, 1);
            MarkCount++;
        }

        /// <summary>
        /// Stores one reading.
        ///
        /// Called once per audio buffer from the audio thread, and once per
        /// silence check from the interface thread. Does nothing at all unless
        /// recording, so the call can be left in place permanently.
        /// </summary>
        /// <param name="timeSeconds">Seconds since capture started.</param>
        /// <param name="audioPresent">
        /// False when this reading came from the silence check rather than from
        /// a real buffer. Worth recording rather than leaving a gap, because a
        /// gap in the timeline and a passage of silence look identical
        /// afterwards and mean quite different things.
        /// </param>
        /// <param name="rawBands">
        /// The seven band strengths straight out of the transform. THE
        /// IMPORTANT ONES - this is the detector's whole input.
        /// </param>
        /// <param name="bandLevels">
        /// The same seven after smoothing and automatic gain, which is what the
        /// wall actually shows. Recorded so a trace can be lined up against what
        /// was seen on screen.
        /// </param>
        public void Record(
            double timeSeconds,
            bool audioPresent,
            double rms,
            double peak,
            double[] rawBands,
            double[] bandLevels,
            double flux,
            double threshold,
            double triggerRatio,
            bool beatDetected,
            double sensitivity,
            double bpm,
            double confidence,
            double trust,
            double beatPhase)
        {
            if (!IsRecording)
            {
                return;
            }

            // Taken before the lock so the mark is claimed exactly once even if
            // both threads arrive together.
            bool marked = Interlocked.Exchange(ref _markPending, 0) == 1;

            lock (_gate)
            {
                if (!IsRecording || _values is null)
                {
                    return;
                }

                int start = _readingCount * ValuesPerReading;

                if (start + ValuesPerReading > _values.Length)
                {
                    // Out of room. Stop rather than wrap around: half an hour of
                    // a set with the beginning missing is far less use than the
                    // first ten minutes intact, and silently overwriting would
                    // make the timestamps in the file non-monotonic.
                    IsRecording = false;
                    ReachedLimit = true;
                    return;
                }

                int at = start;

                _values[at++] = timeSeconds;
                _values[at++] = audioPresent ? 1.0 : 0.0;
                _values[at++] = rms;
                _values[at++] = peak;

                for (int band = 0; band < FrequencyBands.Count; band++)
                {
                    _values[at++] = band < rawBands.Length ? rawBands[band] : 0.0;
                }

                for (int band = 0; band < FrequencyBands.Count; band++)
                {
                    _values[at++] = band < bandLevels.Length ? bandLevels[band] : 0.0;
                }

                _values[at++] = flux;
                _values[at++] = threshold;
                _values[at++] = triggerRatio;
                _values[at++] = beatDetected ? 1.0 : 0.0;
                _values[at++] = sensitivity;
                _values[at++] = bpm;
                _values[at++] = confidence;
                _values[at++] = trust;
                _values[at++] = beatPhase;
                _values[at++] = marked ? 1.0 : 0.0;

                _readingCount++;
            }
        }

        /// <summary>
        /// The heading for each column, in the order Record writes them.
        /// </summary>
        private static string ColumnHeadings()
        {
            var headings = new StringBuilder();

            headings.Append("time,audio,rms,peak");

            // Raw first, because they are the ones that matter for replaying a
            // recording through a different detector.
            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                headings.Append(",raw").Append(band);
            }

            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                headings.Append(",level").Append(band);
            }

            headings.Append(",flux,threshold,trigger,beat,sensitivity,bpm,confidence,trust,phase,mark");

            return headings.ToString();
        }

        /// <summary>
        /// Turns the recording into comma-separated text, ready to be written to
        /// a file.
        ///
        /// Produced here rather than written to disk here, so that this class
        /// stays free of file paths and can be tested without touching the
        /// filesystem. Whoever wants a file asks for the text and saves it.
        ///
        /// Numbers are written with InvariantCulture, which means a full stop for
        /// the decimal point whatever the machine is set to. On a machine
        /// configured for a comma decimal separator the default would produce
        /// "0,42" in a comma-separated file, quietly turning one column into two
        /// and corrupting every row.
        /// </summary>
        public string ToCsv()
        {
            lock (_gate)
            {
                var text = new StringBuilder();

                // A few lines of context first, so a file still makes sense
                // months later or in somebody else's hands. Comment lines start
                // with # and are skipped by anything reading this back.
                text.Append("# LightWall analysis recording").AppendLine();
                text.Append("# readings: ").Append(_readingCount).AppendLine();
                text.Append("# seconds: ")
                    .Append(SecondsRecordedUnlocked().ToString("F2", CultureInfo.InvariantCulture))
                    .AppendLine();
                text.Append("# bands: ").Append(FrequencyBands.Count).AppendLine();

                for (int band = 0; band < FrequencyBands.Count; band++)
                {
                    text.Append("#   ").Append(band).Append(' ')
                        .Append(FrequencyBands.GetName(band)).Append(' ')
                        .Append(FrequencyBands.GetLowEdgeHz(band).ToString(CultureInfo.InvariantCulture))
                        .Append('-')
                        .Append(FrequencyBands.GetHighEdgeHz(band).ToString(CultureInfo.InvariantCulture))
                        .Append(" Hz")
                        .AppendLine();
                }

                if (ReachedLimit)
                {
                    text.Append("# NOTE: stopped early, ran out of room").AppendLine();
                }

                text.Append(ColumnHeadings()).AppendLine();

                if (_values is null)
                {
                    return text.ToString();
                }

                for (int reading = 0; reading < _readingCount; reading++)
                {
                    int start = reading * ValuesPerReading;

                    for (int column = 0; column < ValuesPerReading; column++)
                    {
                        if (column > 0)
                        {
                            text.Append(',');
                        }

                        // Six decimal places: band strengths are small numbers
                        // and rounding them harder would lose the detail the
                        // whole recording exists to capture.
                        text.Append(_values[start + column].ToString("0.######", CultureInfo.InvariantCulture));
                    }

                    text.AppendLine();
                }

                return text.ToString();
            }
        }

        /// <summary>
        /// The seconds covered, for use when the lock is already held.
        /// </summary>
        private double SecondsRecordedUnlocked()
        {
            if (_values is null || _readingCount < 2)
            {
                return 0.0;
            }

            return _values[(_readingCount - 1) * ValuesPerReading] - _values[0];
        }
    }
}
