using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// Something that supplies a live reading of what the music is doing.
    ///
    /// The same arrangement as IWallTransport: the contract lives in Core so the
    /// engine and the tests can work with it, while the implementation that
    /// knows about Windows audio lives in LightWall.IO.
    ///
    /// That split earns its keep in two ways. Core stays free of any platform
    /// knowledge, and tests can supply a fake source producing whatever levels
    /// they like - which is the only practical way to test audio-reactive
    /// behaviour, since a test cannot rely on music actually playing.
    /// </summary>
    public interface IAudioSource : IDisposable
    {
        /// <summary>
        /// A human-readable name for what is being listened to, such as the
        /// name of the sound device. Shown in the interface.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// True when audio is being captured.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// The most recent reading.
        ///
        /// Safe to read from any thread at any time. Audio arrives on its own
        /// thread and publishes complete, unchangeable snapshots, so a reader
        /// always gets a coherent picture of one moment rather than a mixture of
        /// two.
        /// </summary>
        AudioFeatures CurrentFeatures { get; }

        /// <summary>
        /// The most recent problem, or null if nothing has gone wrong.
        ///
        /// Kept rather than thrown, because failures happen on the audio thread
        /// where an exception has nowhere useful to go - but the interface still
        /// needs to be able to say what happened.
        /// </summary>
        string? LastError { get; }

        /// <summary>
        /// Begins capturing.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops capturing.
        /// </summary>
        void Stop();
    }
}
