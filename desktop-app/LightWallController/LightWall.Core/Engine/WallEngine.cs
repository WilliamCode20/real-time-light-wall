using System;
using LightWall.Core.Audio;
using LightWall.Core.Effects;
using LightWall.Core.Models;

namespace LightWall.Core.Engine
{
    /// <summary>
    /// Keeps track of what the wall is currently doing and works out what it
    /// should look like at any given moment.
    ///
    /// This class is the heart of the application.
    ///
    /// WHAT IT REPLACED
    ///
    /// All of this logic used to live inside MainWindow.xaml.cs, mixed in with
    /// button handlers and drawing code. That worked, but it meant the rules of
    /// the wall were tangled up with the rules of the window.
    ///
    /// Pulling it out here matters for a practical reason: this project has
    /// three consumers of "what should the wall look like right now?", and all
    /// three exist today
    ///
    ///   1. the simulator on screen
    ///   2. the physical wall, over the serial cable
    ///   3. the tests
    ///
    /// If that logic lives inside a window, only the window can use it. Tests
    /// cannot run it without opening a window, and the serial layer would have
    /// to reach into the user interface to find out what to send. Neither is
    /// reasonable, so it lives here instead, in a class that knows nothing about
    /// windows or wires.
    ///
    /// TWO MODES
    ///
    /// The engine is always in exactly one of two states:
    ///
    ///   Playing  - an effect is active and paints the wall on every update.
    ///   Manual   - no effect is active; the wall holds whatever the user
    ///              clicked, and updates leave it alone.
    ///
    /// Clicking a cell drops into Manual mode. Choosing an effect returns to
    /// Playing mode.
    ///
    /// A NOTE ON THREADS - AND WHY THIS CLASS STAYS SIMPLE
    ///
    /// This class is not safe to use from several threads at once, and that is
    /// deliberate rather than an oversight waiting to be fixed.
    ///
    /// Three threads do want at it - the show clock ticking it, the window
    /// drawing it, the output service sampling it - but none of them touches it
    /// directly. WallShowClock owns this object outright and is the only thing
    /// that may reach in; everything else goes through Modify, which takes a
    /// lock first, or CopyCurrentFrameTo, which hands out a copy.
    ///
    /// So the locking lives in exactly one place instead of being sprinkled
    /// through here. Do not add locks to this class - that would put the same
    /// protection in two layers, and the second one would be the harder to
    /// reason about.
    /// </summary>
    public sealed class WallEngine
    {
        /// <summary>
        /// The largest time step the engine will accept in one update, in seconds.
        ///
        /// WHY THIS IS NEEDED
        ///
        /// The engine advances by however much real time has passed. Usually
        /// that is a tiny amount, around 1/60th of a second.
        ///
        /// But if the app is paused at a breakpoint in the debugger, or the
        /// laptop is put to sleep, the next update might report that twenty
        /// minutes went by. Feeding that in unchecked would make every animation
        /// leap forward to a completely unrelated point.
        ///
        /// Capping it means a long interruption looks like a brief pause, which
        /// is far less alarming than the wall suddenly teleporting.
        /// </summary>
        private const double MaximumDeltaSeconds = 0.25;

        /// <summary>
        /// Scratch space that the active effect paints into.
        ///
        /// This is kept separate from the output frame so that the Center X/Y
        /// offsets can be applied afterwards without the effect ever needing to
        /// know they exist.
        /// </summary>
        private readonly WallFrame _effectFrame = new();

        /// <summary>
        /// The finished article: what the wall should actually show right now,
        /// with offsets already applied.
        ///
        /// This is the frame the simulator draws and the frame the output service
        /// samples and sends.
        /// </summary>
        private readonly WallFrame _outputFrame = new();

        /// <summary>
        /// Used to pick a fresh seed each time an effect starts, so that random
        /// effects look different on each run rather than repeating identically.
        /// </summary>
        private readonly Random _seedSource = new();

        /// <summary>
        /// How long the current effect has been playing, in "effect seconds".
        ///
        /// This is not the same as real elapsed seconds. The speed setting is
        /// applied as time is accumulated, so at 200% speed this climbs twice as
        /// fast as the clock on the wall does.
        /// </summary>
        private double _effectTimeSeconds;

        /// <summary>
        /// The random seed for the current effect run. Changes each time a new
        /// effect is started.
        /// </summary>
        private int _sessionSeed;

        /// <summary>
        /// The settings the user can adjust with sliders while watching.
        ///
        /// The window writes into this object directly, so slider movements are
        /// picked up on the very next frame.
        /// </summary>
        public EffectParameters Parameters { get; } = new();

        /// <summary>
        /// The effect currently playing, or null when in manual mode.
        /// </summary>
        public IWallEffect? ActiveEffect { get; private set; }

        /// <summary>
        /// The wall state to display or transmit.
        ///
        /// This returns the engine's own working frame rather than a copy, so
        /// reading it is free. The trade-off is that the contents change under
        /// the reader's feet on the next update. Anything needing a stable
        /// snapshot should copy it with CopyFrom.
        /// </summary>
        public WallFrame CurrentFrame => _outputFrame;

        /// <summary>
        /// True when an effect is playing, false in manual mode.
        /// </summary>
        public bool IsPlaying => ActiveEffect is not null;

        /// <summary>
        /// How long the current effect has been running, in effect seconds.
        /// Exposed mainly so tests and future timeline features can inspect it.
        /// </summary>
        public double EffectTimeSeconds => _effectTimeSeconds;

        /// <summary>
        /// How fast effects play, where 1.0 is normal speed.
        ///
        /// 2.0 plays everything twice as fast, 0.5 half as fast.
        ///
        /// HOW THIS DIFFERS FROM BEFORE
        ///
        /// Speed used to be applied by changing how often the timer fired.
        /// It is now applied by changing how quickly effect time accumulates.
        ///
        /// That sounds like the same thing but behaves better in two ways.
        /// The screen keeps refreshing smoothly no matter how slow the animation
        /// is, and changing speed mid-animation adjusts the pace from that point
        /// onward instead of causing a visible jump.
        /// </summary>
        public double SpeedMultiplier { get; set; } = 1.0;

        /// <summary>
        /// What the music is doing, handed to effects when they draw.
        ///
        /// The engine does no analysis and knows nothing about sound cards. It
        /// simply carries whatever it was last given through to the effects, in
        /// the same way it carries the slider settings.
        ///
        /// WallShowClock refreshes this on every tick from whatever audio source
        /// is attached. Tests set it directly, which is what makes
        /// audio-reactive behaviour testable without any music playing.
        /// </summary>
        public AudioFeatures CurrentAudio { get; set; } = AudioFeatures.Silence;

        /// <summary>
        /// Whether audio capture is running.
        ///
        /// See EffectContext.IsAudioActive for why this is kept separate from
        /// the level simply being zero.
        /// </summary>
        public bool IsAudioActive { get; set; }

        /// <summary>
        /// Shifts the picture down (positive) or up (negative) before display.
        /// This is what the Center Y slider controls.
        /// </summary>
        public int OffsetRows { get; set; }

        /// <summary>
        /// Shifts the picture right (positive) or left (negative) before display.
        /// This is what the Center X slider controls.
        /// </summary>
        public int OffsetColumns { get; set; }

        /// <summary>
        /// Starts playing an effect from its beginning.
        ///
        /// The first frame is drawn immediately rather than waiting for the next
        /// update, so the wall responds the instant a button is pressed.
        /// </summary>
        public void Play(IWallEffect effect)
        {
            ActiveEffect = effect ?? throw new ArgumentNullException(nameof(effect));

            // Rewind to the start so the effect begins from its opening frame.
            _effectTimeSeconds = 0.0;

            // A new seed means random effects differ from run to run. Click
            // Sparkle twice and you get two different arrangements.
            _sessionSeed = _seedSource.Next();

            RenderCurrentFrame();
        }

        /// <summary>
        /// Stops the active effect and switches to manual mode.
        ///
        /// The wall deliberately keeps showing whatever was on it at the moment
        /// of stopping, rather than going dark. Stopping is for inspecting a
        /// frame; blanking the wall is what the Clear effect is for.
        /// </summary>
        public void Stop()
        {
            ActiveEffect = null;
            _effectTimeSeconds = 0.0;
        }

        /// <summary>
        /// Moves time forward and repaints the wall.
        ///
        /// This is the method the window's timer calls on every tick.
        ///
        /// It is given the real elapsed time since the previous call rather than
        /// assuming a fixed interval. That matters because timers are not
        /// precise - a timer asked to fire every 16 milliseconds will sometimes
        /// take 20, or 35 if the computer is busy. Measuring the real gap keeps
        /// animations running at a steady pace regardless.
        ///
        /// In manual mode this does nothing, leaving the user's clicked pattern
        /// undisturbed.
        /// </summary>
        public void Advance(double deltaSeconds)
        {
            if (ActiveEffect is null)
            {
                return;
            }

            // Ignore negative gaps. Time should never run backwards, but a
            // sloppy caller should not be able to rewind an animation.
            if (deltaSeconds < 0.0)
            {
                return;
            }

            // Cap enormous gaps. See MaximumDeltaSeconds above for why.
            double safeDelta = Math.Min(deltaSeconds, MaximumDeltaSeconds);

            // Apply the speed setting here, as time is accumulated, so that
            // everything downstream can ignore speed completely.
            _effectTimeSeconds += safeDelta * SpeedMultiplier;

            RenderCurrentFrame();
        }

        /// <summary>
        /// Flips one bulb on or off by hand, and switches to manual mode.
        ///
        /// Manual mode is automatic here because leaving an effect running while
        /// the user clicks cells would mean their change gets painted over
        /// within a fraction of a second - which would look broken.
        /// </summary>
        public void ToggleCell(int row, int column)
        {
            Stop();
            _outputFrame.ToggleCell(row, column);
        }

        /// <summary>
        /// Replaces the wall contents by hand and switches to manual mode.
        ///
        /// Intended for future features that set the wall from an outside
        /// source, such as loading a saved frame.
        /// </summary>
        public void SetFrameManually(WallFrame frame)
        {
            if (frame is null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            Stop();
            _outputFrame.CopyFrom(frame);
        }

        /// <summary>
        /// Asks the active effect what the wall looks like at the current time,
        /// then applies the offset sliders to produce the final output frame.
        ///
        /// The two-stage arrangement is what keeps effects simple: an effect
        /// draws its picture in its natural position and never has to think
        /// about where the user has dragged it to.
        /// </summary>
        private void RenderCurrentFrame()
        {
            if (ActiveEffect is null)
            {
                return;
            }

            // Stage 1: the effect paints its picture.
            var context = new EffectContext(
                _effectTimeSeconds,
                Parameters,
                _sessionSeed,
                CurrentAudio,
                IsAudioActive);
            ActiveEffect.Render(context, _effectFrame);

            // Stage 2: shift it according to the offset sliders.
            //
            // When both offsets are zero - the usual case - a plain copy does
            // the same job with less work, so take that shortcut.
            if (OffsetRows == 0 && OffsetColumns == 0)
            {
                _outputFrame.CopyFrom(_effectFrame);
            }
            else
            {
                _outputFrame.CopyTranslatedFrom(_effectFrame, OffsetRows, OffsetColumns);
            }
        }
    }
}
