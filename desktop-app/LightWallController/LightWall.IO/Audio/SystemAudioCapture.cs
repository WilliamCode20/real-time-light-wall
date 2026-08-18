using System;
using System.Diagnostics;
using LightWall.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LightWall.IO.Audio
{
    /// <summary>
    /// Listens to whatever this computer is playing and reports how loud it is.
    ///
    /// WHAT "LOOPBACK" MEANS, AND WHY IT MATTERS
    ///
    /// This captures the sound going OUT to the speakers, not the sound coming
    /// IN from a microphone. Windows calls that loopback capture.
    ///
    /// It is the right choice here for several reasons. It hears the music
    /// exactly as mixed, with no room acoustics, no echo and no delay. It does
    /// not pick up people talking near the wall. And it works with whatever is
    /// producing the sound - Spotify, a DJ program, a browser tab - with nothing
    /// to configure.
    ///
    /// The alternative, a microphone, would be worse in every one of those ways.
    /// It would also let the wall react to a dropped glass.
    ///
    /// THE THREADING ARRANGEMENT
    ///
    /// Windows delivers audio on its own thread, calling us whenever a buffer is
    /// ready - roughly a hundred times a second. That thread must not be kept
    /// waiting, or audio glitches.
    ///
    /// So the work done there is deliberately tiny: measure the buffer, update
    /// the smoothing, build a snapshot, swap it in. No locks are taken at all.
    ///
    /// The swap works without locking because AudioFeatures can never change
    /// once created. Replacing a reference is a single indivisible operation, so
    /// a reader either sees the whole previous snapshot or the whole new one -
    /// never a mixture. This is the same principle as everywhere else in the
    /// project: share copies, never mutable state.
    /// </summary>
    public sealed class SystemAudioCapture : IAudioSource
    {
        /// <summary>
        /// How long without any audio before we decide nothing is playing.
        ///
        /// WHY SILENCE NEEDS DETECTING AT ALL
        ///
        /// Windows does not send buffers of zeros when nothing is playing. It
        /// sends nothing whatsoever - the callbacks simply stop.
        ///
        /// So "quiet" and "stopped" arrive looking completely different, and
        /// without noticing the second one the level would freeze at whatever it
        /// was when the music ended, leaving the wall holding a pose forever.
        ///
        /// A fifth of a second is far longer than the gap between normal
        /// buffers, so this only triggers on real silence.
        /// </summary>
        private const double SilenceTimeoutSeconds = 0.2;

        /// <summary>
        /// Does all the analysis - loudness, smoothing, automatic gain and the
        /// frequency split. Only ever touched on the audio thread.
        ///
        /// Everything it does is arithmetic and lives in Core, so it can be
        /// tested with no sound card and nothing playing. What is left in this
        /// class is only the plumbing.
        /// </summary>
        private readonly AudioAnalyser _analyser = new();

        /// <summary>
        /// Measures the gap between buffers, so smoothing stays correct
        /// regardless of buffer size.
        /// </summary>
        private readonly Stopwatch _clock = new();

        /// <summary>The NAudio capture object, or null when stopped.</summary>
        private WasapiLoopbackCapture? _capture;

        /// <summary>
        /// The latest snapshot.
        ///
        /// Marked volatile because it is written on the audio thread and read on
        /// others. Without it the compiler could cache the value in a register
        /// and a reader might never notice it had changed.
        /// </summary>
        private volatile AudioFeatures _latest = AudioFeatures.Silence;

        /// <summary>When the last buffer arrived, in seconds on _clock.</summary>
        private double _lastBufferSeconds;

        /// <inheritdoc />
        public string Name { get; private set; } = "System audio";

        /// <inheritdoc />
        public bool IsRunning { get; private set; }

        /// <inheritdoc />
        public AudioFeatures CurrentFeatures => _latest;

        /// <inheritdoc />
        public string? LastError { get; private set; }

        /// <summary>
        /// How quickly the overall level rises. See AudioLevelTracker.
        /// </summary>
        public double AttackSeconds
        {
            get => _analyser.Level.AttackSeconds;
            set => _analyser.Level.AttackSeconds = value;
        }

        /// <summary>
        /// How slowly the overall level falls. See AudioLevelTracker.
        /// </summary>
        public double ReleaseSeconds
        {
            get => _analyser.Level.ReleaseSeconds;
            set => _analyser.Level.ReleaseSeconds = value;
        }

        /// <summary>
        /// A manual multiplier on top of the automatic volume adjustment,
        /// applying to the overall level and every frequency band alike.
        ///
        /// 1.0 leaves it as measured. Higher makes the wall bump harder; lower
        /// makes it more restrained. This is what the Sensitivity slider sets.
        /// </summary>
        public double Sensitivity
        {
            get => _analyser.Sensitivity;
            set => _analyser.Sensitivity = value;
        }

        /// <summary>
        /// How settled the wall looks, from 0 (raw and twitchy) to 1 (slow and
        /// flowing). This is what the Smoothing slider sets.
        /// </summary>
        public double Smoothing
        {
            get => _analyser.Smoothing;
            set => _analyser.Smoothing = value;
        }

        /// <summary>
        /// How big a jump has to be before it counts as a beat, as a multiple of
        /// how big the jumps in this music have been lately.
        ///
        /// Higher finds only the obvious hits; lower finds more, and eventually
        /// starts reporting ordinary texture as beats. This is what the Beat
        /// Sensitivity slider sets. See OnsetDetector.Sensitivity.
        ///
        /// Note that this is a completely separate thing from the Sensitivity
        /// property above, which is about how hard the bars bump. They are named
        /// alike because both are called sensitivity in ordinary speech, but one
        /// changes how the wall reacts to loudness and this one changes what
        /// counts as a beat at all.
        /// </summary>
        public double BeatSensitivity
        {
            get => _analyser.Onsets.Sensitivity;
            set => _analyser.Onsets.Sensitivity = value;
        }

        /// <summary>
        /// Whether the detector keeps its own beat sensitivity in a workable
        /// range instead of leaving it to the slider.
        ///
        /// See OnsetDetector.AutoSensitivity. When this is on, reading
        /// BeatSensitivity reports whatever it has settled on.
        /// </summary>
        public bool AutoBeatSensitivity
        {
            get => _analyser.Onsets.AutoSensitivity;
            set => _analyser.Onsets.AutoSensitivity = value;
        }

        /// <summary>
        /// The shortest gap allowed between two beats, in seconds.
        ///
        /// Raise it if one drum hit is being reported as two or three. See
        /// OnsetDetector.MinimumSecondsBetweenBeats.
        /// </summary>
        public double MinimumSecondsBetweenBeats
        {
            get => _analyser.Onsets.MinimumSecondsBetweenBeats;
            set => _analyser.Onsets.MinimumSecondsBetweenBeats = value;
        }

        /// <summary>
        /// How close the most recent reading came to counting as a beat, where
        /// 1.0 means it was exactly on the line. Shown as a meter while tuning.
        /// See OnsetDetector.TriggerRatio, which explains what this can and
        /// cannot tell you.
        /// </summary>
        public double BeatTriggerRatio => _analyser.Onsets.TriggerRatio;

        // WHY THE SETTINGS ABOVE ARE SAFE TO CHANGE FROM THE INTERFACE
        //
        // Those properties cross a thread boundary: the window writes them while
        // the audio thread is reading them, with no lock anywhere.
        //
        // That is fine here, and it is worth saying why rather than assuming it.
        // Each is a single number, so a reader gets either the old value or the
        // new one, never a half-written mixture. And nothing reads two of them
        // together and needs them to agree - each guard is applied on its own.
        // The worst case is that one audio buffer out of a hundred is judged
        // with the old setting, which nobody can hear or see.
        //
        // This is the same reasoning that already covers the Sensitivity and
        // Smoothing sliders, which have worked this way from the start.
        //
        // It would NOT be safe if a setting needed several values changed
        // together to stay consistent. If that ever comes up, the fix is to swap
        // in a whole new settings object by reference, the way AudioFeatures
        // snapshots already do - not to start adding locks to the audio path.

        /// <summary>
        /// The reference level the automatic adjustment is currently working
        /// against. Shown in the interface, because seeing it move is the
        /// clearest way to tell the adjustment is doing something.
        /// </summary>
        public double GainReference => _analyser.GainReference;

        /// <summary>
        /// Starts listening to the default playback device.
        /// </summary>
        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            LastError = null;

            try
            {
                // Whatever Windows is currently using for playback. Following
                // the default means the app picks up a change of output device
                // next time it starts, with nothing to configure.
                using (var devices = new MMDeviceEnumerator())
                {
                    MMDevice device = devices.GetDefaultAudioEndpoint(
                        DataFlow.Render,
                        Role.Multimedia);

                    Name = device.FriendlyName;
                }

                _capture = new WasapiLoopbackCapture();
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                // Tell the analyser what rate this device mixes at, so it knows
                // which frequency each transform output corresponds to. Get this
                // wrong and every band would be reading the wrong frequencies.
                _analyser.SampleRate = _capture.WaveFormat.SampleRate;

                _analyser.Reset();
                _latest = AudioFeatures.Silence;

                _clock.Restart();
                _lastBufferSeconds = 0.0;

                _capture.StartRecording();
                IsRunning = true;
            }
            catch (Exception ex)
            {
                LastError = $"Could not start audio capture: {ex.Message}";
                Cleanup();
            }
        }

        /// <summary>
        /// Stops listening.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            try
            {
                _capture?.StopRecording();
            }
            catch (Exception ex)
            {
                LastError = $"Error while stopping audio capture: {ex.Message}";
            }

            Cleanup();
        }

        /// <summary>
        /// Lets the level decay when no audio has arrived recently.
        ///
        /// Must be called regularly by whatever is displaying or using the
        /// level, because the audio thread stops calling us entirely when
        /// nothing is playing - and "nothing arriving" is exactly the condition
        /// that needs noticing.
        /// </summary>
        public void UpdateIdle()
        {
            if (!IsRunning)
            {
                return;
            }

            double now = _clock.Elapsed.TotalSeconds;
            double sinceLastBuffer = now - _lastBufferSeconds;

            if (sinceLastBuffer < SilenceTimeoutSeconds)
            {
                return;
            }

            // Nothing has arrived for a while, so ease the level down. The gap
            // is measured from the previous check rather than from the last
            // buffer, so the decay runs at a steady rate however often this is
            // called.
            _lastBufferSeconds = now;
            _latest = _analyser.ProcessSilence(sinceLastBuffer);
        }

        /// <summary>
        /// Releases the capture device.
        /// </summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// Called by Windows whenever a buffer of audio is ready.
        ///
        /// This runs on the audio thread and must be quick. Everything it does
        /// is arithmetic over a few hundred numbers, with no allocation beyond
        /// one small snapshot object and no locking.
        /// </summary>
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                WaveFormat? format = _capture?.WaveFormat;

                if (format is null || e.BytesRecorded == 0)
                {
                    return;
                }

                // WASAPI loopback hands over 32-bit floating point samples,
                // which is what Windows mixes in internally. Anything else would
                // need converting, so say so plainly rather than producing
                // silently wrong readings.
                if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32)
                {
                    LastError =
                        $"Unexpected audio format: {format.Encoding}, {format.BitsPerSample} bits. " +
                        "Expected 32-bit IEEE float.";
                    return;
                }

                // Reinterpret the raw bytes as floats. Four bytes per sample.
                //
                // All channels are treated as one long list rather than being
                // separated. For a loudness reading that is fine and slightly
                // better than picking one channel, since it averages the stereo
                // image - a sound panned hard left still registers.
                ReadOnlySpan<float> samples = System.Runtime.InteropServices.MemoryMarshal
                    .Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));

                double now = _clock.Elapsed.TotalSeconds;
                double delta = now - _lastBufferSeconds;
                _lastBufferSeconds = now;

                // Hand the raw samples to Core and let it do everything -
                // loudness, smoothing, automatic gain, the frequency split.
                //
                // Build a new snapshot and swap it in. Readers on other threads
                // see either the old one or this one, never a mixture.
                _latest = _analyser.Process(samples, format.Channels, delta);
            }
            catch (Exception ex)
            {
                // An exception escaping here would take down the audio thread,
                // silently ending capture with no way back short of restarting.
                // Record it and carry on.
                LastError = $"Audio processing error: {ex.Message}";
            }
        }

        /// <summary>
        /// Called when recording stops, whether we asked for it or not.
        ///
        /// It can happen without being asked - unplugging a USB sound card, or
        /// Windows switching default device mid-session.
        /// </summary>
        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception is not null)
            {
                LastError = $"Audio capture stopped unexpectedly: {e.Exception.Message}";
            }

            IsRunning = false;
            _latest = AudioFeatures.Silence;
        }

        /// <summary>
        /// Detaches from and disposes of the capture object.
        /// </summary>
        private void Cleanup()
        {
            if (_capture is not null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                _capture = null;
            }

            IsRunning = false;
            _clock.Stop();
            _latest = AudioFeatures.Silence;
        }
    }
}
