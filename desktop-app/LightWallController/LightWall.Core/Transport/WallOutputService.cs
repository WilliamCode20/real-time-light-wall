using System;
using System.Diagnostics;
using System.Threading;
using LightWall.Core.Engine;
using LightWall.Core.Models;
using LightWall.Core.Serialization;

namespace LightWall.Core.Transport
{
    /// <summary>
    /// Takes frames from the show clock and sends them to a wall, at a rate the
    /// hardware can actually cope with.
    ///
    /// This is the last stage of the pipeline:
    ///
    ///   WallShowClock -> WallOutputService -> IWallTransport -> the wall
    ///
    /// THE RATE LIMIT IS THE POINT
    ///
    /// The engine ticks at around 120 times a second and the window draws at
    /// about 60. The relays can do nothing like that.
    ///
    /// The original hand-written show gives us real evidence: its fastest effect
    /// held a bulb for 15 milliseconds and the installation ran it correctly, so
    /// about 30 updates a second is comfortable and 60 is at the proven edge.
    /// This service therefore samples the wall 30 times a second and ignores
    /// everything in between.
    ///
    /// LATEST FRAME WINS - THERE IS NO QUEUE
    ///
    /// When it is time to send, this asks the clock what the wall looks like
    /// RIGHT NOW. Frames generated between sends are not stored, not queued,
    /// just skipped.
    ///
    /// That is deliberate and it matters. If frames were queued, then any moment
    /// where the wall could not keep up would leave a backlog, and the wall
    /// would start showing the past. The backlog would grow, and the lag would
    /// get steadily worse until nothing on the wall had anything to do with the
    /// music. Dropping frames means the wall is always at worst one frame behind
    /// reality, permanently, no matter what happens.
    ///
    /// For a display, stale data is worthless. Newest always wins.
    ///
    /// WHY EVERY FRAME IS SENT, EVEN UNCHANGED ONES
    ///
    /// The wall gets a packet 30 times a second whether the picture changed or
    /// not. Skipping unchanged frames would save bandwidth, but sending them
    /// buys two useful things:
    ///
    /// - it is self-healing; a packet lost to a corrupted byte is replaced a
    ///   thirtieth of a second later, rather than leaving the wall wrong until
    ///   the picture happens to change
    /// - it keeps the firmware's watchdog fed automatically, so a still frame
    ///   holds on the wall without needing separate heartbeats
    ///
    /// The cost is negligible: 9 bytes at 30 per second is 270 bytes a second,
    /// which is about 2% of a 115200 baud connection.
    /// </summary>
    public sealed class WallOutputService : IDisposable
    {
        /// <summary>
        /// How often the measured send-rate readout is refreshed, in seconds.
        /// </summary>
        private const double RateSampleIntervalSeconds = 0.5;

        /// <summary>
        /// How long the loop sleeps between checks, in milliseconds.
        ///
        /// Deliberately much shorter than the gap between sends. The loop wakes
        /// often and usually decides it is not time yet, which keeps the actual
        /// send timing close to the target instead of being coarsened by the
        /// sleep.
        /// </summary>
        private const int LoopSleepMilliseconds = 2;

        /// <summary>Guards the transport and the counters.</summary>
        private readonly object _gate = new();

        /// <summary>Where frames come from.</summary>
        private readonly WallShowClock _clock;

        /// <summary>
        /// Scratch frame reused for every send, so the loop does not create a
        /// new object 30 times a second.
        ///
        /// Only ever touched by the output thread.
        /// </summary>
        private readonly WallFrame _frameToSend = new();

        /// <summary>Measures time between sends.</summary>
        private readonly Stopwatch _sendClock = new();

        /// <summary>Where frames go. Null when nothing is attached.</summary>
        private IWallTransport? _transport;

        /// <summary>The background thread running the send loop.</summary>
        private Thread? _thread;

        /// <summary>Tells the loop to keep going. See WallShowClock for why volatile.</summary>
        private volatile bool _running;

        /// <summary>When the last packet went out, in seconds on _sendClock.</summary>
        private double _lastSendSeconds;

        /// <summary>Packets sent since the rate readout was last updated.</summary>
        private int _packetsSinceRateSample;

        /// <summary>Seconds elapsed since the rate readout was last updated.</summary>
        private double _secondsSinceRateSample;

        /// <summary>Most recently measured send rate.</summary>
        private double _measuredPacketsPerSecond;

        /// <summary>Total packets sent since attaching.</summary>
        private int _packetsSent;

        /// <summary>
        /// Creates an output service that draws frames from the given clock.
        /// </summary>
        public WallOutputService(WallShowClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// How many packets a second to send.
        ///
        /// Defaults to 30, based on measured behaviour of the real installation.
        /// See the notes on this class, and HARDWARE_NOTES.md, for the evidence.
        ///
        /// Treat higher values with suspicion. The limit here is physical: a
        /// zero-cross relay can only change state when the mains voltage crosses
        /// zero, which is 120 times a second, and asking for changes shorter
        /// than one half cycle produces inconsistent behaviour between bulbs
        /// rather than faster animation.
        /// </summary>
        public double OutputRateHz { get; set; } = 30.0;

        /// <summary>True when a transport is attached and packets are flowing.</summary>
        public bool IsSending
        {
            get
            {
                lock (_gate)
                {
                    return _running && _transport is { IsConnected: true };
                }
            }
        }

        /// <summary>The attached transport, or null.</summary>
        public IWallTransport? Transport
        {
            get
            {
                lock (_gate)
                {
                    return _transport;
                }
            }
        }

        /// <summary>Total packets sent since the current transport was attached.</summary>
        public int PacketsSent
        {
            get { lock (_gate) { return _packetsSent; } }
        }

        /// <summary>
        /// The send rate actually being achieved, refreshed twice a second.
        /// </summary>
        public double MeasuredPacketsPerSecond
        {
            get { lock (_gate) { return _measuredPacketsPerSecond; } }
        }

        /// <summary>
        /// Connects a transport and starts sending frames to it.
        ///
        /// Any previously attached transport is detached first, so this doubles
        /// as a way to switch from the virtual wall to a real one.
        /// </summary>
        public void Attach(IWallTransport transport)
        {
            if (transport is null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            Detach();

            transport.Connect();

            lock (_gate)
            {
                _transport = transport;
                _packetsSent = 0;
                _packetsSinceRateSample = 0;
                _secondsSinceRateSample = 0.0;
                _measuredPacketsPerSecond = 0.0;
            }

            _sendClock.Restart();
            _lastSendSeconds = 0.0;

            StartLoop();
        }

        /// <summary>
        /// Stops sending and closes the transport.
        ///
        /// A blackout packet goes out first, so the wall goes dark rather than
        /// freezing on whatever frame happened to be showing when output stopped.
        /// Leaving bulbs lit with nothing driving them is exactly the situation
        /// the firmware watchdog exists to clean up, and it is better not to
        /// need rescuing in the first place.
        /// </summary>
        public void Detach()
        {
            StopLoop();

            IWallTransport? transport;

            lock (_gate)
            {
                transport = _transport;
                _transport = null;
            }

            if (transport is null)
            {
                return;
            }

            try
            {
                if (transport.IsConnected)
                {
                    transport.Send(WallFrameSerializer.CreateBlackoutPacket());
                }
            }
            catch (Exception)
            {
                // A transport that has already failed - an unplugged cable, say -
                // will throw here. There is nothing useful to do about it while
                // shutting down, and letting it escape would stop the port from
                // being closed below, which would be worse.
            }

            transport.Disconnect();
        }

        /// <summary>
        /// Sends one packet immediately, outside the normal rate-limited flow.
        ///
        /// Intended for one-off commands such as a manual blackout, or the
        /// bulb-by-bulb identification routine used to check the wiring.
        /// </summary>
        public void SendImmediate(byte[] packet)
        {
            if (packet is null)
            {
                throw new ArgumentNullException(nameof(packet));
            }

            lock (_gate)
            {
                if (_transport is { IsConnected: true })
                {
                    _transport.Send(packet);
                    _packetsSent++;
                }
            }
        }

        /// <summary>
        /// Stops everything and releases the transport.
        /// </summary>
        public void Dispose()
        {
            Detach();
        }

        /// <summary>
        /// Starts the background send loop.
        /// </summary>
        private void StartLoop()
        {
            if (_running)
            {
                return;
            }

            _running = true;

            _thread = new Thread(RunSendLoop)
            {
                IsBackground = true,
                Name = "LightWall output"
            };

            _thread.Start();
        }

        /// <summary>
        /// Stops the background send loop and waits for it to finish.
        /// </summary>
        private void StopLoop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            _thread?.Join(TimeSpan.FromSeconds(2));
            _thread = null;
        }

        /// <summary>
        /// The send loop, running on its own background thread.
        ///
        /// Rather than sleeping for exactly the gap between sends, it wakes
        /// frequently and checks whether enough time has passed. That makes it
        /// robust against the sleep being imprecise: a sleep that overruns
        /// delays one packet slightly instead of permanently lowering the rate.
        ///
        /// More importantly, it guarantees the rate is never EXCEEDED, which is
        /// the direction that matters. Sending a little slower than 30 a second
        /// is harmless. Sending faster risks asking the relays for something
        /// they cannot physically do.
        /// </summary>
        private void RunSendLoop()
        {
            while (_running)
            {
                double nowSeconds = _sendClock.Elapsed.TotalSeconds;
                double minimumInterval = 1.0 / OutputRateHz;

                if (nowSeconds - _lastSendSeconds >= minimumInterval)
                {
                    double actualInterval = nowSeconds - _lastSendSeconds;
                    _lastSendSeconds = nowSeconds;

                    SendCurrentFrame(actualInterval);
                }

                Thread.Sleep(LoopSleepMilliseconds);
            }
        }

        /// <summary>
        /// Asks the clock what the wall looks like now, and sends it.
        /// </summary>
        private void SendCurrentFrame(double intervalSeconds)
        {
            // Ask for the state at this instant. Anything generated since the
            // last send is skipped rather than queued - see the class notes.
            _clock.CopyCurrentFrameTo(_frameToSend);

            byte[] packet = WallFrameSerializer.CreateFramePacket(_frameToSend);

            lock (_gate)
            {
                if (_transport is not { IsConnected: true })
                {
                    return;
                }

                try
                {
                    _transport.Send(packet);
                    _packetsSent++;
                    UpdateMeasuredRate(intervalSeconds);
                }
                catch (Exception)
                {
                    // A failed send - most likely an unplugged cable - must not
                    // bring the thread down, because that would silently stop
                    // all output with no way back short of restarting the app.
                    //
                    // Dropping the packet and trying again in a thirtieth of a
                    // second is the right response: if the cable comes back,
                    // output resumes by itself.
                }
            }
        }

        /// <summary>
        /// Keeps a running measurement of the real send rate.
        ///
        /// Always called with the lock already held.
        /// </summary>
        private void UpdateMeasuredRate(double intervalSeconds)
        {
            _packetsSinceRateSample++;
            _secondsSinceRateSample += intervalSeconds;

            if (_secondsSinceRateSample < RateSampleIntervalSeconds)
            {
                return;
            }

            _measuredPacketsPerSecond = _packetsSinceRateSample / _secondsSinceRateSample;

            _packetsSinceRateSample = 0;
            _secondsSinceRateSample = 0.0;
        }
    }
}
