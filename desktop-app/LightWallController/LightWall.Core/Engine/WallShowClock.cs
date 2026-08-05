using System;
using System.Diagnostics;
using System.Threading;
using LightWall.Core.Audio;
using LightWall.Core.Effects;
using LightWall.Core.Models;

namespace LightWall.Core.Engine
{
    /// <summary>
    /// Runs the wall engine on its own background thread, independently of the
    /// user interface.
    ///
    /// WHY THE ENGINE NEEDED ITS OWN CLOCK
    ///
    /// Until now the engine was advanced by the window's redraw loop. That was
    /// fine for a simulator and wrong for a light controller, for three reasons:
    ///
    /// 1. The screen and the wall want different rates. The simulator redraws
    ///    around 60 times a second; the relays cannot be driven anywhere near
    ///    that fast and want roughly 30. Neither should have to compromise for
    ///    the other, and they cannot both be the redraw loop.
    ///
    /// 2. Writing to a serial port can block. If that happened on the interface
    ///    thread, a USB hiccup would freeze the whole window.
    ///
    /// 3. The wall's timing should not depend on the window being healthy. A
    ///    busy interface, a drag operation, or a slow redraw should not change
    ///    what the physical wall is doing.
    ///
    /// WHAT THIS CLASS OWNS
    ///
    /// The engine, and exclusive rights to touch it. Everyone else - the window,
    /// the output service - goes through the methods here, which take a lock
    /// first. That way the engine itself stays a simple single-threaded class
    /// and never has to think about threads at all.
    ///
    /// ABOUT THE LOCK
    ///
    /// A "lock" means only one thread at a time may run the code inside it. The
    /// others wait their turn.
    ///
    /// That sounds expensive but is not, here: a tick advances some arithmetic
    /// and sets 35 booleans, which takes a few microseconds. The window might
    /// wait a few microseconds to change a slider value. Nothing noticeable.
    ///
    /// The alternative - letting several threads read and write the engine at
    /// once - produces bugs that appear randomly, cannot be reproduced on
    /// demand, and are among the hardest kinds to track down. A lock this cheap
    /// is a bargain.
    /// </summary>
    public sealed class WallShowClock : IDisposable
    {
        /// <summary>
        /// How often the readouts of measured tick rate are refreshed, in
        /// seconds.
        /// </summary>
        private const double RateSampleIntervalSeconds = 0.5;

        /// <summary>
        /// The object threads take turns holding. It guards every access to
        /// the engine below, without exception.
        /// </summary>
        private readonly object _gate = new();

        /// <summary>
        /// The engine. Never touch this outside a lock on _gate.
        /// </summary>
        private readonly WallEngine _engine = new();

        /// <summary>Measures real elapsed time between ticks.</summary>
        private readonly Stopwatch _clock = new();

        /// <summary>The background thread running the tick loop.</summary>
        private Thread? _thread;

        /// <summary>
        /// Tells the loop to keep going.
        ///
        /// Marked "volatile" because it is written by one thread and read by
        /// another. Without that, the compiler is entitled to assume nobody else
        /// changes it and cache the value in a register - meaning the loop could
        /// carry on running forever after being asked to stop.
        /// </summary>
        private volatile bool _running;

        /// <summary>Ticks counted since the rate readout was last updated.</summary>
        private int _ticksSinceRateSample;

        /// <summary>Seconds elapsed since the rate readout was last updated.</summary>
        private double _secondsSinceRateSample;

        /// <summary>Most recently measured tick rate.</summary>
        private double _measuredTicksPerSecond;

        /// <summary>
        /// How often the engine is advanced, in ticks per second.
        ///
        /// This is a target rather than a promise. Windows timers are only
        /// accurate to roughly 15 milliseconds by default, so the real rate is
        /// often lower - check MeasuredTicksPerSecond for what is actually
        /// happening.
        ///
        /// Crucially, that does not matter for correctness. The engine is
        /// advanced by measured elapsed time, so animations run at the right
        /// pace whatever rate this lands at. A lower rate means slightly
        /// chunkier motion, not slower motion.
        ///
        /// 120 is chosen as a comfortable margin above both consumers: the
        /// window drawing at about 60 and the wall being fed at about 30.
        /// </summary>
        public double TickRateHz { get; init; } = 120.0;

        /// <summary>
        /// Where the music is listened to, or null when nothing is attached.
        ///
        /// The clock reads the latest reading on every tick and hands it to the
        /// engine, which passes it through to whichever effect is playing.
        ///
        /// No lock is needed to read from it. Audio snapshots can never change
        /// once created and are swapped in as whole objects, so this thread
        /// always sees a complete picture of one moment. That is the same
        /// principle used everywhere else in the project: share copies, never
        /// mutable state.
        /// </summary>
        public IAudioSource? AudioSource { get; set; }

        /// <summary>True once Start has been called and the loop is running.</summary>
        public bool IsRunning => _running;

        /// <summary>
        /// The tick rate actually being achieved, refreshed twice a second.
        /// </summary>
        public double MeasuredTicksPerSecond
        {
            get
            {
                lock (_gate)
                {
                    return _measuredTicksPerSecond;
                }
            }
        }

        /// <summary>The effect currently playing, or null in manual mode.</summary>
        public IWallEffect? ActiveEffect
        {
            get
            {
                lock (_gate)
                {
                    return _engine.ActiveEffect;
                }
            }
        }

        /// <summary>True when an effect is playing, false in manual mode.</summary>
        public bool IsPlaying
        {
            get
            {
                lock (_gate)
                {
                    return _engine.IsPlaying;
                }
            }
        }

        /// <summary>
        /// Starts the background tick loop.
        /// </summary>
        public void Start()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _clock.Restart();

            _thread = new Thread(RunTickLoop)
            {
                // A background thread does not keep the program alive. Without
                // this, closing the window would leave the process running
                // invisibly because this loop never finished.
                IsBackground = true,

                Name = "LightWall show clock"
            };

            _thread.Start();
        }

        /// <summary>
        /// Stops the tick loop and waits for it to finish.
        /// </summary>
        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;

            // Wait for the loop to notice and exit, so that by the time this
            // method returns the thread really has stopped. A generous timeout
            // guards against hanging shutdown if something goes wrong.
            _thread?.Join(TimeSpan.FromSeconds(2));
            _thread = null;

            _clock.Stop();
        }

        /// <summary>
        /// Performs an operation on the engine, safely.
        ///
        /// This is the ONLY way anything outside this class should change the
        /// engine. Hand it a job and it will run it while holding the lock.
        ///
        /// Example, from the window:
        ///
        ///   clock.Modify(engine =&gt; engine.Play(effect));
        ///   clock.Modify(engine =&gt; engine.SpeedMultiplier = 1.5);
        ///
        /// The odd-looking "engine =&gt; ..." is a lambda: a small piece of code
        /// passed as an argument, to be run later by somebody else. It is the
        /// same idea as the pattern-drawing routines handed to
        /// StaticPatternEffect.
        ///
        /// Why one general method rather than a wrapper for each operation:
        /// there would be a dozen wrappers, all identical apart from the line in
        /// the middle, and every new engine feature would need another one.
        ///
        /// Keep the work inside short. The tick loop is waiting.
        /// </summary>
        public void Modify(Action<WallEngine> change)
        {
            if (change is null)
            {
                throw new ArgumentNullException(nameof(change));
            }

            lock (_gate)
            {
                change(_engine);
            }
        }

        /// <summary>
        /// Copies the wall's current state into a frame the caller owns.
        ///
        /// A copy rather than a reference, deliberately. Handing out the
        /// engine's own frame would mean the contents changed under the reader
        /// mid-use, so a drawing routine could paint the top half of one frame
        /// and the bottom half of the next.
        ///
        /// Both the window and the output service call this - each on their own
        /// schedule, each getting a clean snapshot of the same wall.
        /// </summary>
        public void CopyCurrentFrameTo(WallFrame destination)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            lock (_gate)
            {
                destination.CopyFrom(_engine.CurrentFrame);
            }
        }

        /// <summary>
        /// Advances the engine by hand, without the background loop.
        ///
        /// Only for tests, which want to control time exactly rather than wait
        /// for real seconds to pass.
        /// </summary>
        public void AdvanceManually(double deltaSeconds)
        {
            lock (_gate)
            {
                _engine.Advance(deltaSeconds);
            }
        }

        /// <summary>
        /// Stops the loop when the clock is disposed of.
        /// </summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// The tick loop, running on the background thread.
        ///
        /// It measures how much real time has passed, advances the engine by
        /// exactly that, then sleeps for roughly the remainder of the tick
        /// period.
        ///
        /// Measuring rather than assuming is what makes the sleep's imprecision
        /// harmless. A sleep asked for 8 milliseconds might take 15; the next
        /// tick simply advances by 15 milliseconds' worth and the animation
        /// stays exactly on pace.
        /// </summary>
        private void RunTickLoop()
        {
            double lastElapsedSeconds = _clock.Elapsed.TotalSeconds;

            // Work out roughly how long to sleep between ticks. At least 1, both
            // to avoid a zero-length sleep spinning the processor at full tilt
            // and because sleeps shorter than that are not honoured anyway.
            int sleepMilliseconds = Math.Max(1, (int)Math.Round(1000.0 / TickRateHz));

            while (_running)
            {
                double nowSeconds = _clock.Elapsed.TotalSeconds;
                double deltaSeconds = nowSeconds - lastElapsedSeconds;
                lastElapsedSeconds = nowSeconds;

                // Read the audio outside the lock. It costs nothing and takes no
                // lock of its own, so there is no reason to make the interface
                // thread wait behind it.
                IAudioSource? audio = AudioSource;
                AudioFeatures features = audio?.CurrentFeatures ?? AudioFeatures.Silence;
                bool audioRunning = audio?.IsRunning ?? false;

                lock (_gate)
                {
                    _engine.CurrentAudio = features;
                    _engine.IsAudioActive = audioRunning;

                    _engine.Advance(deltaSeconds);
                    UpdateMeasuredRate(deltaSeconds);
                }

                Thread.Sleep(sleepMilliseconds);
            }
        }

        /// <summary>
        /// Keeps a running measurement of the real tick rate.
        ///
        /// Always called with the lock already held.
        /// </summary>
        private void UpdateMeasuredRate(double deltaSeconds)
        {
            _ticksSinceRateSample++;
            _secondsSinceRateSample += deltaSeconds;

            if (_secondsSinceRateSample < RateSampleIntervalSeconds)
            {
                return;
            }

            _measuredTicksPerSecond = _ticksSinceRateSample / _secondsSinceRateSample;

            _ticksSinceRateSample = 0;
            _secondsSinceRateSample = 0.0;
        }
    }
}
