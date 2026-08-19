using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Turns raw sound into everything the visual side needs to know about it.
    ///
    /// This is the single front door to audio analysis. Hand it a buffer of
    /// samples, get back a complete snapshot: overall loudness, peak, and the
    /// strength of each frequency band.
    ///
    /// WHY THE WHOLE ANALYSIS LIVES IN CORE
    ///
    /// Everything here is arithmetic. None of it needs Windows, a sound card, or
    /// anything playing. That means all of it can be tested against signals
    /// whose answers are known in advance - feed in a pure 100 Hz tone and check
    /// that the bass band lights up while the treble stays dark.
    ///
    /// What is left in LightWall.IO is only the plumbing: asking Windows for
    /// buffers and passing them here. That is the part that genuinely cannot be
    /// tested without hardware, and it is now about ten lines.
    ///
    /// THE SHAPE THIS SETS UP
    ///
    /// Analysis happens once per buffer, not once per effect. Everything that
    /// wants to react to the music reads the same snapshot through
    /// EffectContext.
    ///
    /// Adding a new effect touches no audio code. Adding a new measurement -
    /// beat detection, tempo, whatever comes next - touches no effect code, and
    /// every existing effect can use it immediately. That separation is what
    /// keeps this manageable as the number of effects grows.
    /// </summary>
    public sealed class AudioAnalyser
    {
        /// <summary>
        /// Tracks overall loudness across the whole spectrum.
        /// </summary>
        public AudioLevelTracker Level { get; } = new();

        /// <summary>
        /// Splits the sound into bands, one per wall column.
        /// </summary>
        public SpectrumAnalyser Spectrum { get; }

        /// <summary>
        /// Spots the moment new sounds start - drum hits, chords, stabs.
        /// </summary>
        public OnsetDetector Onsets { get; } = new();

        /// <summary>
        /// Works out the tempo from the timing of those onsets.
        /// </summary>
        public TempoEstimator Tempo { get; } = new();

        /// <summary>
        /// A metronome running at that tempo, which keeps counting through quiet
        /// passages where there is nothing to detect.
        /// </summary>
        public BeatClock Clock { get; } = new();

        /// <summary>
        /// Writes every reading down for study afterwards, or null for the
        /// normal case of not recording.
        ///
        /// Attached from outside rather than owned here, because whether to
        /// record is a question for whoever is running the app, not for the
        /// analyser. Left null this costs one null check per buffer.
        ///
        /// See AnalysisRecorder for why a recording of the band strengths is a
        /// complete record of what the beat detector had to work with, rather
        /// than a summary of it.
        /// </summary>
        public AnalysisRecorder? Recorder { get; set; }

        /// <summary>When the metronome last struck.</summary>
        private double _lastPulseSeconds = double.NegativeInfinity;

        /// <summary>The pulse count at the previous update, to spot new ones.</summary>
        private int _previousPulseCount;

        /// <summary>
        /// Running total of time since capture started.
        ///
        /// Beat detection needs absolute times rather than gaps, because it
        /// reasons about how long ago things happened.
        /// </summary>
        private double _elapsedSeconds;

        /// <summary>When the last beat was detected.</summary>
        private double _lastBeatSeconds = double.NegativeInfinity;

        /// <summary>How many beats have been detected since capture started.</summary>
        private int _beatCount;

        /// <summary>
        /// Creates an analyser for audio at a given sample rate.
        /// </summary>
        public AudioAnalyser(int sampleRate = 48000)
        {
            Spectrum = new SpectrumAnalyser(sampleRate);
        }

        /// <summary>
        /// Samples per second of the incoming audio.
        ///
        /// Settable because the default playback device can change while the app
        /// is running, and a different device may mix at a different rate. Get
        /// this wrong and every band would be reading the wrong frequencies.
        /// </summary>
        public int SampleRate
        {
            get => Spectrum.SampleRate;
            set => Spectrum.SampleRate = value;
        }

        /// <summary>
        /// How hard the wall responds, on top of the automatic adjustment.
        /// Applies to the overall level and to every band alike.
        /// </summary>
        public double Sensitivity
        {
            get => Level.Gain.Gain;
            set
            {
                Level.Gain.Gain = value;
                Spectrum.Sensitivity = value;
            }
        }

        /// <summary>
        /// How settled the wall looks, from 0 (raw and twitchy) to 1 (slow and
        /// flowing). See SpectrumAnalyser.Smoothing.
        /// </summary>
        public double Smoothing
        {
            get => Spectrum.Smoothing;
            set => Spectrum.Smoothing = value;
        }

        /// <summary>
        /// The reference the overall automatic adjustment is working against.
        /// Shown in the interface, since watching it move is the clearest sign
        /// the adjustment is doing something.
        /// </summary>
        public double GainReference => Level.Gain.Reference;

        /// <summary>
        /// Analyses one buffer of audio and produces a complete snapshot.
        /// </summary>
        /// <param name="interleavedSamples">
        /// Samples as they arrive from the sound system, channels alternating.
        /// </param>
        /// <param name="channels">How many channels are interleaved.</param>
        /// <param name="deltaSeconds">Time since the previous buffer.</param>
        public AudioFeatures Process(
            ReadOnlySpan<float> interleavedSamples,
            int channels,
            double deltaSeconds)
        {
            _elapsedSeconds += deltaSeconds;

            (double rms, double peak) = AudioSampleMath.Analyse(interleavedSamples);

            Spectrum.AddSamples(interleavedSamples, channels);
            double[] bands = Spectrum.Analyse(deltaSeconds);

            // The automatic sensitivity tuner needs to know how fast the music is
            // before it can know how many detections a second to aim for. Handed
            // over rather than worked out inside the detector, which has no
            // business estimating tempo.
            //
            // Safe when the tempo is wrong, which matters because it often is
            // early in a track: a hint can only ever ask for MORE detections
            // than the fixed floor, never fewer. See OnsetDetector.HealthyRange
            // for the feedback loop that would otherwise trap the tuner at a
            // wrong answer.
            Onsets.TempoHintBpm = Tempo.Bpm;

            // Which point in the beat we are at, so the band weighting can ask
            // which bands keep arriving at the same one. See BandBeatAgreement.
            Onsets.BeatPhaseHint = Clock.Phase;
            Onsets.TempoConfidenceHint = Tempo.Confidence;

            // Beat detection works from the RAW band strengths, not the smoothed
            // ones. Smoothing rounds off exactly the sharp rise an onset
            // consists of.
            bool beatHeard = Onsets.Update(Spectrum.GetRawStrengths(), _elapsedSeconds);

            if (beatHeard)
            {
                _lastBeatSeconds = _elapsedSeconds;
                _beatCount++;
                Tempo.AddBeat(_elapsedSeconds);
            }

            Tempo.Update(_elapsedSeconds);
            AdvanceClock(deltaSeconds, beatHeard);

            AudioFeatures features =
                Level.Update(rms, peak, deltaSeconds, bands, BuildBeatInfo());

            // Written down last, once everything for this reading has settled,
            // so a recorded row is a coherent picture of one moment rather than
            // a mixture of this reading and the previous one. Costs nothing when
            // nothing is recording. See AnalysisRecorder.
            Recorder?.Record(
                _elapsedSeconds,
                audioPresent: true,
                rms,
                peak,
                Spectrum.GetRawStrengths(),
                bands,
                Onsets.CurrentFlux,
                Onsets.CurrentThreshold,
                Onsets.TriggerRatio,
                beatHeard,
                Onsets.Sensitivity,
                Tempo.Bpm,
                Tempo.Confidence,
                Tempo.Trust,
                Clock.Phase);

            return features;
        }

        /// <summary>
        /// Moves the metronome on, and nudges it toward any beat just heard.
        /// </summary>
        private void AdvanceClock(double deltaSeconds, bool beatHeard)
        {
            Clock.Update(deltaSeconds, Tempo.Bpm);

            if (beatHeard)
            {
                Clock.SyncToDetectedBeat();
            }

            // Notice when the metronome has struck, so the time since can be
            // reported. Comparing counts rather than watching the phase, since
            // an unusually long gap could carry the phase past 1 more than once.
            if (Clock.PulseCount != _previousPulseCount)
            {
                _previousPulseCount = Clock.PulseCount;
                _lastPulseSeconds = _elapsedSeconds;
            }
        }

        /// <summary>
        /// Records that no audio arrived, letting everything decay.
        ///
        /// Needed because Windows sends nothing at all during silence rather
        /// than sending zeros - so "nothing is playing" arrives as an absence
        /// rather than as data, and has to be noticed rather than received.
        /// </summary>
        public AudioFeatures ProcessSilence(double deltaSeconds)
        {
            _elapsedSeconds += deltaSeconds;

            double[] bands = Spectrum.AnalyseSilence(deltaSeconds);

            Tempo.Update(_elapsedSeconds);

            // The metronome keeps counting through silence on purpose. That is
            // the whole point of having it: a breakdown with nothing playing
            // should still pulse in time.
            AdvanceClock(deltaSeconds, beatHeard: false);

            AudioFeatures features =
                Level.UpdateSilent(deltaSeconds, bands, BuildBeatInfo());

            // Silence is recorded too, rather than left as a gap in the file.
            // A gap and a quiet passage look identical afterwards and mean
            // completely different things - one is the music, the other is the
            // recording having missed something.
            //
            // The raw strengths are whatever the last real buffer left behind,
            // since nothing new arrived to replace them. That is honest: no
            // measurement was taken, and the "audio" column says so.
            Recorder?.Record(
                _elapsedSeconds,
                audioPresent: false,
                rms: 0.0,
                peak: 0.0,
                Spectrum.GetRawStrengths(),
                bands,
                Onsets.CurrentFlux,
                Onsets.CurrentThreshold,
                Onsets.TriggerRatio,
                beatDetected: false,
                Onsets.Sensitivity,
                Tempo.Bpm,
                Tempo.Confidence,
                Tempo.Trust,
                Clock.Phase);

            return features;
        }

        /// <summary>
        /// Gathers the current beat information for a snapshot.
        /// </summary>
        private BeatInfo BuildBeatInfo()
        {
            double sinceBeat = double.IsNegativeInfinity(_lastBeatSeconds)
                ? AudioFeatures.NoBeatYet
                : _elapsedSeconds - _lastBeatSeconds;

            double sincePulse = double.IsNegativeInfinity(_lastPulseSeconds)
                ? AudioFeatures.NoBeatYet
                : _elapsedSeconds - _lastPulseSeconds;

            return new BeatInfo(
                sinceBeat,
                _beatCount,
                Tempo.Bpm,
                Tempo.Confidence,
                sincePulse,
                Clock.PulseCount,
                Clock.Phase);
        }

        /// <summary>
        /// Forgets all history and starts again.
        /// </summary>
        public void Reset()
        {
            Level.Reset();
            Spectrum.Reset();
            Onsets.Reset();
            Tempo.Reset();
            Clock.Reset();

            _elapsedSeconds = 0.0;
            _lastBeatSeconds = double.NegativeInfinity;
            _beatCount = 0;
            _lastPulseSeconds = double.NegativeInfinity;
            _previousPulseCount = 0;
        }
    }
}
