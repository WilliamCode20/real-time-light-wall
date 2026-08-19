using System;
using System.Collections.Generic;

namespace LightWall.Core.Audio
{
    /// <summary>
    /// A snapshot of what the music was doing at one instant.
    ///
    /// This is the bridge between the audio side of the app and the visual side.
    /// Audio capture produces these; effects read them through EffectContext the
    /// same way they read elapsed time.
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
        public static readonly AudioFeatures Silence =
            new(0.0, 0.0, 0.0, 0.0, new double[FrequencyBands.Count], isSilent: true);

        /// <summary>
        /// How strong each frequency band is. See BandLevels.
        /// </summary>
        private readonly double[] _bandLevels;

        /// <summary>
        /// Creates a snapshot.
        /// </summary>
        /// <param name="bandLevels">
        /// One level per frequency band. The array is taken as-is rather than
        /// copied, so whoever supplies it must never change it afterwards -
        /// which is the whole basis on which these snapshots are safe to share
        /// between threads.
        /// </param>
        public AudioFeatures(
            double rms,
            double peak,
            double level,
            double normalisedLevel,
            double[] bandLevels,
            bool isSilent,
            double secondsSinceBeat = NoBeatYet,
            int beatCount = 0,
            double tempoBpm = 0.0,
            double tempoConfidence = 0.0,
            double secondsSincePulse = NoBeatYet,
            int pulseCount = 0,
            double beatPhase = 0.0,
            double tempoStability = 0.0)
        {
            SecondsSincePulse = secondsSincePulse;
            PulseCount = pulseCount;
            BeatPhase = beatPhase;
            TempoStability = tempoStability;
            Rms = rms;
            Peak = peak;
            Level = level;
            NormalisedLevel = normalisedLevel;
            IsSilent = isSilent;
            SecondsSinceBeat = secondsSinceBeat;
            BeatCount = beatCount;
            TempoBpm = tempoBpm;
            TempoConfidence = tempoConfidence;

            _bandLevels = bandLevels ?? throw new ArgumentNullException(nameof(bandLevels));

            if (_bandLevels.Length != FrequencyBands.Count)
            {
                throw new ArgumentException(
                    $"Expected {FrequencyBands.Count} band levels, got {_bandLevels.Length}.",
                    nameof(bandLevels));
            }
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
        /// This is absolute loudness: turn the computer's volume down and this
        /// goes down with it. Good for a meter that should show how loud things
        /// really are.
        ///
        /// Raw RMS would be a poor choice even for that, for two reasons this
        /// fixes. It jitters frame to frame, which would make anything driven by
        /// it flicker. And human hearing is logarithmic while RMS is linear, so
        /// ordinary music spends nearly all its time crammed into the bottom
        /// tenth of the range.
        ///
        /// See AudioLevelTracker for how both are dealt with.
        /// </summary>
        public double Level { get; }

        /// <summary>
        /// Loudness measured against the recent loudest moment, from 0 to 1.
        ///
        /// THIS IS THE ONE TO DRIVE VISUALS WITH.
        ///
        /// Unlike Level, this barely cares where the volume knob is set. It
        /// asks "how loud is this compared to the loudest thing lately?" rather
        /// than "how loud is this compared to the loudest sound possible", so
        /// the wall behaves the same at half volume as at full.
        ///
        /// It also has a response curve applied, which spreads quiet and loud
        /// further apart so the bars use the whole height of the wall instead of
        /// hovering around the middle.
        ///
        /// See AudioGainController for both.
        /// </summary>
        public double NormalisedLevel { get; }

        /// <summary>
        /// How strong each frequency band is right now, from 0 to 1.
        ///
        /// Band 0 is the lowest - the thump of a kick drum - and band 6 the
        /// highest, where cymbals live. There is one per wall column, so band N
        /// naturally drives column N.
        ///
        /// Each band is measured against its own recent history rather than
        /// against the others. That matters enormously: bass typically carries a
        /// hundred times the energy of treble, so measured against a shared
        /// reference the high columns would never move at all. Measured against
        /// itself, a quiet hi-hat is loud *for a hi-hat* and lights its column
        /// properly.
        /// </summary>
        public IReadOnlyList<double> BandLevels => _bandLevels;

        /// <summary>
        /// Reads one band's level, or 0 if asked for a band that does not exist.
        ///
        /// Forgiving on purpose. Effects index this with a column number, and a
        /// wall of a different width should produce a dark column rather than a
        /// crash.
        /// </summary>
        public double GetBandLevel(int band)
        {
            if (band < 0 || band >= _bandLevels.Length)
            {
                return 0.0;
            }

            return _bandLevels[band];
        }

        /// <summary>
        /// The value SecondsSinceBeat carries when no beat has been heard.
        ///
        /// A large number rather than a null or a separate flag, so that an
        /// effect asking "was there a beat in the last tenth of a second?" gets
        /// a sensible "no" without needing to check anything first.
        /// </summary>
        public const double NoBeatYet = 9999.0;

        /// <summary>
        /// How long ago the last beat was detected, in seconds.
        ///
        /// THE ONE TO DRIVE BEAT-REACTIVE EFFECTS WITH.
        ///
        /// Deliberately a time rather than a "beat happened" flag. A flag would
        /// be true for one audio buffer only, and the engine reads these
        /// snapshots on its own schedule - so a flag could be missed entirely,
        /// or seen twice and counted as two beats.
        ///
        /// A time works whatever the rates involved: "flash for the first tenth
        /// of a second after a beat" means the same thing regardless of how
        /// often anyone looks.
        /// </summary>
        public double SecondsSinceBeat { get; }

        /// <summary>
        /// How many beats have been detected since capture started.
        ///
        /// Only ever increases. An effect wanting to do something once per beat
        /// - rather than for a stretch after each one - can compare this against
        /// what it saw last time.
        /// </summary>
        public int BeatCount { get; }

        /// <summary>
        /// How long ago the metronome last struck, in seconds.
        ///
        /// THE ONE FOR EFFECTS THAT SHOULD KEEP TIME THROUGH QUIET PASSAGES.
        ///
        /// Unlike SecondsSinceBeat, this does not depend on anything being
        /// played. Once the tempo is known the metronome keeps counting, so a
        /// breakdown with nothing but a pad still pulses in time.
        ///
        /// The trade is honesty: this can be wrong in a way SecondsSinceBeat
        /// cannot, because it is a prediction rather than an observation. If the
        /// tempo estimate is off, this drifts.
        /// </summary>
        public double SecondsSincePulse { get; }

        /// <summary>
        /// How many metronome pulses have happened since counting began.
        /// </summary>
        public int PulseCount { get; }

        /// <summary>
        /// How far through the current beat, from 0 (just landed) to 1 (the next
        /// is due).
        ///
        /// For effects that want to sweep, fade or travel across a beat rather
        /// than merely blink on it.
        /// </summary>
        public double BeatPhase { get; }

        /// <summary>
        /// The estimated tempo in beats per minute, or 0 when unknown.
        ///
        /// Note that tempo is genuinely ambiguous: the same music at 70 and at
        /// 140 are both correct descriptions, and listeners disagree about this
        /// constantly. A slow track may be reported at double its written tempo.
        /// See TempoEstimator.
        /// </summary>
        public double TempoBpm { get; }

        /// <summary>
        /// How consistent the recent beats have been, from 0 to 1.
        ///
        /// Worth having alongside the tempo, because a confident wrong answer
        /// and an unconfident one look identical without it.
        /// </summary>
        public double TempoConfidence { get; }

        /// <summary>
        /// How long the tempo estimate has held still, from 0 to 1.
        ///
        /// A DIFFERENT QUESTION FROM CONFIDENCE, AND USUALLY THE MORE USEFUL ONE.
        ///
        /// Confidence asks what share of recent sounds land on the beat, which
        /// plenty of correct answers score badly on - real music is full of
        /// off-beat content. This asks only whether the answer has stopped
        /// moving.
        ///
        /// It is what "the tempo looks solid now" should be judged on. See
        /// TempoEstimator.Stability, including why being blind to whether the
        /// answer is right is safe here.
        /// </summary>
        public double TempoStability { get; }

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
