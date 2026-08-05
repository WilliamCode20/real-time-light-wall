using System;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// A snapshot of what the music was doing at one instant.
    ///
    /// This is the bridge between the audio side of the app and the visual side.
    /// Audio capture produces these; effects will eventually read them the same
    /// way they read time today.
    ///
    /// WHY THIS IS IMMUTABLE
    ///
    /// Every value is set once when the object is created and can never change
    /// afterwards. That is deliberate, and it is what makes handing these
    /// between threads safe without any locking.
    ///
    /// Audio arrives on its own thread - Windows calls us whenever a buffer of
    /// sound is ready - while the engine reads on a different one. Sharing a
    /// mutable object between them would mean the engine could read Rms from one
    /// moment and Peak from the next, producing a snapshot that never actually
    /// happened.
    ///
    /// Because these can never change, the audio thread can simply build a new
    /// one and swap it in. A reader either sees the whole old snapshot or the
    /// whole new one, never a mixture. No lock required.
    /// </summary>
    public sealed class AudioFeatures
    {
        /// <summary>
        /// A snapshot representing silence, used before any audio has arrived
        /// and whenever nothing is playing.
        /// </summary>
        public static readonly AudioFeatures Silence = new(0.0, 0.0, 0.0, isSilent: true);

        /// <summary>
        /// Creates a snapshot.
        /// </summary>
        public AudioFeatures(double rms, double peak, double level, bool isSilent)
        {
            Rms = rms;
            Peak = peak;
            Level = level;
            IsSilent = isSilent;
        }

        /// <summary>
        /// The raw average loudness over the last buffer, from 0 to 1.
        ///
        /// "RMS" is root mean square - square every sample, average them, take
        /// the square root. Squaring is what makes it useful: it removes the
        /// distinction between a sample being positive or negative, since a
        /// sound wave swings both ways and both halves are equally loud.
        ///
        /// This tracks perceived loudness far better than simply averaging the
        /// numbers would, since that would average out to roughly zero for any
        /// normal sound.
        /// </summary>
        public double Rms { get; }

        /// <summary>
        /// The loudest single sample in the last buffer, from 0 to 1.
        ///
        /// Reacts instantly to transients - a snare hit, a stab - where RMS
        /// smooths them into the surrounding sound. Useful later for accents.
        /// </summary>
        public double Peak { get; }

        /// <summary>
        /// The smoothed, decibel-mapped loudness, from 0 to 1.
        ///
        /// THIS IS THE ONE TO DRIVE VISUALS WITH.
        ///
        /// Raw RMS is a poor choice for lighting, for two reasons this value
        /// fixes. It jitters frame to frame, which would make anything driven by
        /// it flicker. And human hearing is logarithmic while RMS is linear, so
        /// ordinary music spends nearly all its time crammed into the bottom
        /// tenth of the range - a meter driven by raw RMS barely moves.
        ///
        /// See AudioLevelTracker for how both are dealt with.
        /// </summary>
        public double Level { get; }

        /// <summary>
        /// True when nothing is playing.
        ///
        /// Worth knowing explicitly rather than inferring from a low level.
        /// Windows sends no audio buffers at all when nothing is producing
        /// sound, so "silent" and "very quiet" arrive looking quite different.
        /// </summary>
        public bool IsSilent { get; }
    }
}
