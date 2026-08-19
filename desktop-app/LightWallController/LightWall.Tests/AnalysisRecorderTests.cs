using System;
using System.Globalization;
using System.Linq;
using LightWall.Core.Audio;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the analysis recorder.
    ///
    /// The property that actually matters here is the one in the last test: a
    /// recording of the raw band strengths has to be enough to reproduce what
    /// the detector did, because that is the entire reason the recorder exists.
    /// If a trace cannot be replayed, it is a souvenir rather than a measurement.
    /// </summary>
    public class AnalysisRecorderTests
    {
        private const int SampleRate = 48000;

        /// <summary>
        /// Builds a short burst of noise, standing in for a drum hit.
        /// </summary>
        private static float[] MakeHit(int sampleCount, Random random, double amplitude = 0.7)
        {
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                double envelope = 1.0 - ((double)i / sampleCount);
                samples[i] = (float)((random.NextDouble() * 2.0 - 1.0) * amplitude * envelope);
            }

            return samples;
        }

        /// <summary>
        /// Plays a steady beat through an analyser, optionally recording it.
        /// </summary>
        private static AudioAnalyser PlayBeat(double seconds, AnalysisRecorder? recorder = null)
        {
            var analyser = new AudioAnalyser(SampleRate) { Recorder = recorder };
            var random = new Random(4242);

            const double bufferSeconds = 0.01;
            int bufferSamples = (int)(SampleRate * bufferSeconds);
            double elapsed = 0.0;

            while (elapsed < seconds)
            {
                double intoBeat = elapsed % 0.5;

                float[] buffer = intoBeat < 0.03
                    ? MakeHit(bufferSamples, random)
                    : MakeHit(bufferSamples, random, 0.05);

                analyser.Process(buffer, channels: 1, deltaSeconds: bufferSeconds);
                elapsed += bufferSeconds;
            }

            return analyser;
        }

        [Fact]
        public void ARecorderDoesNothingUntilItIsStarted()
        {
            // The call sits permanently in the analysis path, so not recording
            // has to be the cheap and completely inert case.
            var recorder = new AnalysisRecorder();

            PlayBeat(2.0, recorder);

            Assert.False(recorder.IsRecording);
            Assert.Equal(0, recorder.ReadingCount);
        }

        [Fact]
        public void RecordingCapturesOneReadingPerBuffer()
        {
            var recorder = new AnalysisRecorder();
            recorder.Start();

            PlayBeat(2.0, recorder);
            recorder.Stop();

            // Two seconds of 0.01 s buffers. Allow a little slack rather than
            // demanding exactly 200, since the loop's floating-point accumulator
            // decides how many iterations it gets.
            Assert.InRange(recorder.ReadingCount, 195, 205);
            Assert.InRange(recorder.SecondsRecorded, 1.9, 2.1);
        }

        [Fact]
        public void StoppingKeepsWhatWasGatheredAndTakesNoMore()
        {
            var recorder = new AnalysisRecorder();
            recorder.Start();

            var analyser = PlayBeat(1.0, recorder);
            recorder.Stop();

            int afterStop = recorder.ReadingCount;
            Assert.True(afterStop > 0);

            // Keep feeding the same analyser. Nothing more should be stored.
            var random = new Random(7);

            for (int i = 0; i < 50; i++)
            {
                analyser.Process(MakeHit(480, random), channels: 1, deltaSeconds: 0.01);
            }

            Assert.Equal(afterStop, recorder.ReadingCount);
        }

        [Fact]
        public void SilenceIsRecordedRatherThanLeavingAGap()
        {
            // A gap in the timeline and a passage of silence look identical
            // afterwards and mean completely different things - one is the
            // music, the other is the recording having missed something.
            var recorder = new AnalysisRecorder();
            recorder.Start();

            var analyser = new AudioAnalyser(SampleRate) { Recorder = recorder };

            for (int i = 0; i < 50; i++)
            {
                analyser.ProcessSilence(0.01);
            }

            recorder.Stop();

            Assert.Equal(50, recorder.ReadingCount);

            // ...and every one of them says it was silence rather than audio.
            string[] rows = DataRows(recorder.ToCsv());
            Assert.All(rows, row => Assert.Equal("0", row.Split(',')[1]));
        }

        [Fact]
        public void AMarkLandsOnExactlyOneReading()
        {
            var recorder = new AnalysisRecorder();
            recorder.Start();

            var analyser = new AudioAnalyser(SampleRate) { Recorder = recorder };
            var random = new Random(11);

            for (int i = 0; i < 20; i++)
            {
                analyser.Process(MakeHit(480, random), channels: 1, deltaSeconds: 0.01);
            }

            recorder.Mark();

            for (int i = 0; i < 20; i++)
            {
                analyser.Process(MakeHit(480, random), channels: 1, deltaSeconds: 0.01);
            }

            recorder.Stop();

            string[] rows = DataRows(recorder.ToCsv());
            int marked = rows.Count(row => row.Split(',').Last() == "1");

            // Exactly one, not zero and not smeared across several. A mark that
            // landed twice would be as misleading as one that vanished.
            Assert.Equal(1, marked);
            Assert.Equal(1, recorder.MarkCount);
        }

        [Fact]
        public void StartingAgainThrowsAwayThePreviousTake()
        {
            var recorder = new AnalysisRecorder();

            recorder.Start();
            PlayBeat(1.0, recorder);
            recorder.Stop();

            Assert.True(recorder.ReadingCount > 0);

            recorder.Start();
            Assert.Equal(0, recorder.ReadingCount);
            Assert.Equal(0, recorder.MarkCount);
        }

        [Fact]
        public void TheFileCarriesAHeadingForEveryColumnItWrites()
        {
            var recorder = new AnalysisRecorder();
            recorder.Start();
            PlayBeat(0.5, recorder);
            recorder.Stop();

            string csv = recorder.ToCsv();
            string heading = HeadingRow(csv);
            string[] rows = DataRows(csv);

            int headingColumns = heading.Split(',').Length;

            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.Equal(headingColumns, row.Split(',').Length));

            // The raw band strengths are the load-bearing ones - see the last
            // test - so name them explicitly rather than trusting the count.
            for (int band = 0; band < FrequencyBands.Count; band++)
            {
                Assert.Contains($"raw{band}", heading);
            }
        }

        [Fact]
        public void NumbersAreWrittenWithAFullStopWhateverTheMachineIsSetTo()
        {
            // On a machine configured for a comma decimal separator, the default
            // formatting would produce "0,42" inside a comma-separated file,
            // quietly turning one column into two and corrupting every row.
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;

            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new CultureInfo("de-DE");

                var recorder = new AnalysisRecorder();
                recorder.Start();
                PlayBeat(0.5, recorder);
                recorder.Stop();

                string csv = recorder.ToCsv();
                string heading = HeadingRow(csv);
                int headingColumns = heading.Split(',').Length;

                Assert.All(
                    DataRows(csv),
                    row => Assert.Equal(headingColumns, row.Split(',').Length));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        /// <summary>
        /// THE PROPERTY THE WHOLE THING RESTS ON.
        ///
        /// The beat detector's entire input is the seven raw band strengths and
        /// a timestamp - OnsetDetector.Update takes nothing else. So a recording
        /// of those columns must be enough to drive a detector and get back
        /// exactly what happened at the time, not merely something similar.
        ///
        /// That is what makes a recording an experiment rather than a souvenir:
        /// a candidate algorithm can be run against a real break as many times
        /// as it takes, without the track being replayed.
        ///
        /// This test proves it the decisive way - it replays the recorded
        /// columns through a fresh detector and checks the beats land on exactly
        /// the same readings.
        /// </summary>
        [Fact]
        public void ARecordingCanBeReplayedThroughADetectorAndGiveTheSameBeats()
        {
            var recorder = new AnalysisRecorder();
            recorder.Start();
            PlayBeat(6.0, recorder);
            recorder.Stop();

            string csv = recorder.ToCsv();
            string[] heading = HeadingRow(csv).Split(',');
            string[] rows = DataRows(csv);

            int timeColumn = Array.IndexOf(heading, "time");
            int beatColumn = Array.IndexOf(heading, "beat");
            int firstRawColumn = Array.IndexOf(heading, "raw0");

            var replayed = new OnsetDetector();
            var bands = new double[FrequencyBands.Count];

            int recordedBeats = 0;
            int replayedBeats = 0;
            int disagreements = 0;

            foreach (string row in rows)
            {
                string[] columns = row.Split(',');

                double time = double.Parse(columns[timeColumn], CultureInfo.InvariantCulture);
                bool recordedBeat = columns[beatColumn] == "1";

                for (int band = 0; band < FrequencyBands.Count; band++)
                {
                    bands[band] = double.Parse(
                        columns[firstRawColumn + band], CultureInfo.InvariantCulture);
                }

                bool replayedBeat = replayed.Update(bands, time);

                if (recordedBeat) recordedBeats++;
                if (replayedBeat) replayedBeats++;
                if (recordedBeat != replayedBeat) disagreements++;
            }

            Assert.True(recordedBeats > 5, $"Only {recordedBeats} beats recorded; too few to prove anything.");

            Assert.Equal(recordedBeats, replayedBeats);

            Assert.Equal(0, disagreements);
        }

        /// <summary>
        /// The heading line, skipping the leading comment lines.
        /// </summary>
        private static string HeadingRow(string csv)
        {
            return csv
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .First(line => line.Length > 0 && !line.StartsWith('#'));
        }

        /// <summary>
        /// Every line of actual data - no comments, no heading, no blanks.
        /// </summary>
        private static string[] DataRows(string csv)
        {
            return csv
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Skip(1)
                .ToArray();
        }
    }
}
