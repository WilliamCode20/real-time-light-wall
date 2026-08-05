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
            (double rms, double peak) = AudioSampleMath.Analyse(interleavedSamples);

            Spectrum.AddSamples(interleavedSamples, channels);
            double[] bands = Spectrum.Analyse(deltaSeconds);

            return Level.Update(rms, peak, deltaSeconds, bands);
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
            double[] bands = Spectrum.AnalyseSilence(deltaSeconds);

            return Level.UpdateSilent(deltaSeconds, bands);
        }

        /// <summary>
        /// Forgets all history and starts again.
        /// </summary>
        public void Reset()
        {
            Level.Reset();
            Spectrum.Reset();
        }
    }
}
