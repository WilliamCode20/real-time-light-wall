using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LightWall.Core.Effects;
using LightWall.Core.Engine;
using LightWall.Core.Models;
using LightWall.Core.Serialization;
using LightWall.Core.Transport;
using LightWall.Core.Audio;
using LightWall.IO.Audio;
using LightWall.IO.Serial;
using System.Windows.Shapes;

namespace LightWall.App
{
    /// <summary>
    /// Code-behind for the main simulator window.
    ///
    /// WHAT THIS CLASS IS RESPONSIBLE FOR
    ///
    /// Only three things:
    ///
    ///   1. building the buttons
    ///   2. passing slider values along to the show clock
    ///   3. drawing whatever the clock says the wall looks like
    ///
    /// That is a deliberate reduction. This file used to also decide how
    /// animations played, track which frame was current, apply the offset
    /// sliders, and hold the wall state itself.
    ///
    /// HOW THE PIECES FIT TOGETHER NOW
    ///
    ///   WallShowClock       ticks the engine on its own background thread
    ///        |
    ///        +--> this window       draws it, around 60 times a second
    ///        |
    ///        +--> WallOutputService samples it 30 times a second, builds
    ///                               packets, and hands them to a transport
    ///                                   |
    ///                                   +--> LoopbackTransport (virtual wall)
    ///                                   +--> SerialTransport   (not yet built)
    ///
    /// The important part is that the window is now just one of the clock's
    /// consumers rather than the thing driving everything. The wall keeps
    /// running at its own steady rate no matter what the interface is doing,
    /// and a slow serial write can never freeze the window.
    ///
    /// The rule of thumb going forward is that if a piece of logic would still
    /// make sense with no screen attached, it belongs in LightWall.Core rather
    /// than here.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// How often the frame-rate readout is refreshed, in seconds.
        /// Updating it every frame would make it flicker too fast to read.
        /// </summary>
        private const double FrameRateUpdateIntervalSeconds = 0.5;

        /// <summary>
        /// How often the output statistics readout is refreshed, in seconds.
        /// </summary>
        private const double OutputStatsUpdateIntervalSeconds = 0.25;

        // ------------------------------------------------------------------
        // Brushes
        //
        // These are created once and shared by all 35 buttons, rather than being
        // created fresh on every redraw.
        //
        // The old version made new brush objects inside the drawing loop, which
        // meant roughly 70 short-lived objects per frame - a few thousand every
        // second. .NET copes with that, but it gives the garbage collector
        // needless work, and its cleanup pauses can show up as small stutters.
        //
        // Freeze() marks a brush as permanently unchangeable. WPF can then skip
        // its usual change-tracking bookkeeping, which makes drawing with it
        // faster.
        // ------------------------------------------------------------------

        /// <summary>Warm yellow for a lit bulb.</summary>
        private static readonly Brush LitBackgroundBrush = CreateFrozenBrush(255, 199, 0);

        /// <summary>Dark grey for an unlit bulb.</summary>
        private static readonly Brush UnlitBackgroundBrush = CreateFrozenBrush(70, 70, 70);

        /// <summary>Pale yellow edge, so lit bulbs read as illuminated.</summary>
        private static readonly Brush LitBorderBrush = CreateFrozenBrush(255, 220, 120);

        /// <summary>Mid grey edge for unlit bulbs.</summary>
        private static readonly Brush UnlitBorderBrush = CreateFrozenBrush(110, 110, 110);

        /// <summary>Highlight for the button of the effect currently playing.</summary>
        private static readonly Brush ActiveEffectBrush = CreateFrozenBrush(255, 199, 0);

        /// <summary>Normal background for an effect button that is not playing.</summary>
        private static readonly Brush InactiveEffectBrush = CreateFrozenBrush(221, 221, 221);

        /// <summary>The beat lamp while a beat is being reported.</summary>
        private static readonly Brush BeatLampLitBrush = CreateFrozenBrush(255, 199, 0);

        /// <summary>The beat lamp the rest of the time.</summary>
        private static readonly Brush BeatLampUnlitBrush = CreateFrozenBrush(42, 42, 42);

        /// <summary>
        /// The show clock: owns the engine and runs it on a background thread.
        ///
        /// The window never touches the engine directly any more. It asks the
        /// clock to make changes, and asks the clock what to draw.
        /// </summary>
        private readonly WallShowClock _clock = new();

        /// <summary>
        /// The virtual wall: a software model of the Arduino, standing in for
        /// real hardware.
        ///
        /// Everything downstream of the engine - rate limiting, packet building,
        /// transmission, receiving, validation - runs for real against this. The
        /// only thing it cannot tell us is whether the physical wiring matches
        /// what we believe, which needs the actual wall.
        /// </summary>
        private readonly LoopbackTransport _loopback = new();

        /// <summary>
        /// The real serial connection, or null when only the virtual wall is
        /// running.
        ///
        /// Kept as a field rather than being read back from the output service
        /// because the status readout needs its specifics — whether the board is
        /// still restarting, how many packets got through, what went wrong — and
        /// those live on SerialTransport rather than on the interface it
        /// implements.
        /// </summary>
        private SerialTransport? _serial;

        /// <summary>
        /// Listens to whatever this computer is playing.
        ///
        /// Created once and reused, since starting and stopping it is cheap
        /// while creating it involves asking Windows about audio devices.
        /// </summary>
        private readonly SystemAudioCapture _audio = new();

        /// <summary>
        /// What the trigger meter is currently showing, which is not quite the
        /// same as the latest reading.
        ///
        /// WHY THE METER NEEDS A MEMORY
        ///
        /// The detector produces a reading about a hundred times a second, and
        /// the screen redraws about sixty times a second. So roughly two out of
        /// every five readings are never seen by the meter at all - and the ones
        /// most likely to be missed are the brief spikes, which are the entire
        /// thing being looked for.
        ///
        /// A meter showing only whatever happened to be there at redraw time
        /// would therefore miss a good share of the hits, and would look like
        /// the detector was worse than it is.
        ///
        /// So this rises instantly to any new reading and falls back gradually,
        /// which holds a spike on screen long enough to see. It is the same fast
        /// attack, slow release idea used on the audio level itself, and for the
        /// same reason.
        ///
        /// THE LIMIT THIS DOES NOT FIX
        ///
        /// A spike that begins and ends entirely between two redraws is still
        /// missed - holding a value cannot recover one that was never read. The
        /// lamp beside the meter is what covers that, because it works from a
        /// time since the last beat rather than from a momentary value, so it
        /// cannot fall down the gap between frames. That is why there are two
        /// indicators rather than one.
        /// </summary>
        private double _displayedTriggerRatio;

        /// <summary>
        /// Samples the clock 30 times a second and sends packets to whatever
        /// transport is attached.
        /// </summary>
        private readonly WallOutputService _output;

        /// <summary>
        /// Every effect the app can play. Used to build the buttons.
        /// </summary>
        private readonly EffectCatalog _catalog = new();

        /// <summary>
        /// The window's own copy of the wall state.
        ///
        /// The clock copies into this on each redraw rather than handing over a
        /// reference to its own frame. That matters because the clock's frame is
        /// being rewritten by a different thread roughly 120 times a second - a
        /// drawing routine reading it directly could paint the top half of one
        /// frame and the bottom half of the next.
        /// </summary>
        private readonly WallFrame _displayFrame = new();

        /// <summary>
        /// The window's copy of what the VIRTUAL wall is showing - that is, what
        /// the receiver decoded from the packets that actually arrived.
        ///
        /// In normal running this matches _displayFrame exactly. When bytes are
        /// being dropped or corrupted it falls behind, because damaged packets
        /// are discarded and the wall keeps showing the last good frame until
        /// another one gets through.
        /// </summary>
        private readonly WallFrame _virtualFrame = new();

        /// <summary>
        /// The 35 coloured squares making up the virtual wall display.
        ///
        /// These are Borders rather than Buttons because, unlike the engine
        /// wall, nothing here is clickable. A Border is a plain rectangle with a
        /// colour - lighter than a Button, and without the hover and focus
        /// highlighting that would be misleading on a display that is only ever
        /// reporting what the hardware would be doing.
        /// </summary>
        private readonly Border[] _virtualCells =
            new Border[WallFrame.Rows * WallFrame.Columns];

        /// <summary>
        /// What each virtual cell looked like last time it was drawn, so
        /// unchanged ones can be skipped.
        /// </summary>
        private readonly bool[] _renderedVirtualStates =
            new bool[WallFrame.Rows * WallFrame.Columns];

        /// <summary>
        /// Forces a full repaint of the virtual wall on the next pass. Needed
        /// for the first draw, for the same reason as _forceFullRedraw.
        /// </summary>
        private bool _forceVirtualRedraw = true;

        /// <summary>
        /// Direct references to the 35 wall buttons, stored in row-major order
        /// so button number (row * 7 + column) is the one for that cell.
        ///
        /// Keeping this list means the drawing loop can go straight to a button
        /// instead of walking the grid's children and type-checking each one on
        /// every single frame.
        /// </summary>
        private readonly Button[] _cellButtons =
            new Button[WallFrame.Rows * WallFrame.Columns];

        /// <summary>
        /// What each cell looked like the last time it was drawn.
        ///
        /// This is what lets the drawing loop skip cells that have not changed.
        /// In most frames only a handful of the 35 bulbs actually differ, and
        /// leaving the rest untouched avoids asking WPF to redraw them for no
        /// reason.
        /// </summary>
        private readonly bool[] _renderedCellStates =
            new bool[WallFrame.Rows * WallFrame.Columns];

        /// <summary>
        /// Links each effect to its button, so the button belonging to whatever
        /// is playing can be highlighted.
        /// </summary>
        private readonly Dictionary<IWallEffect, Button> _effectButtons = new();

        /// <summary>
        /// Measures how much real time passes between redraws.
        ///
        /// Stopwatch is used rather than DateTime because it is built for
        /// measuring intervals precisely, and is unaffected by the system clock
        /// being adjusted or by daylight saving changes.
        /// </summary>
        private readonly Stopwatch _frameClock = new();

        /// <summary>
        /// Forces every cell to be redrawn on the next pass, ignoring the
        /// change detection.
        ///
        /// Needed for the very first draw: the buttons start with no colours at
        /// all, but the recorded states start as "all off" and so does the wall,
        /// so nothing would look changed and nothing would be painted.
        /// </summary>
        private bool _forceFullRedraw = true;

        /// <summary>Counts frames drawn since the rate readout was last updated.</summary>
        private int _framesSinceRateUpdate;

        /// <summary>Seconds elapsed since the rate readout was last updated.</summary>
        private double _secondsSinceRateUpdate;

        /// <summary>Seconds elapsed since the output statistics were last updated.</summary>
        private double _secondsSinceOutputStatsUpdate;

        /// <summary>
        /// Constructor for the main window.
        ///
        /// Startup order:
        /// 1. load and connect the XAML
        /// 2. build the 35 wall buttons
        /// 3. build the effect buttons from the catalog
        /// 4. copy the starting slider values into the engine
        /// 5. start the show clock so the engine begins ticking
        /// 6. attach the virtual wall so packets start flowing
        /// 7. draw the initial wall and start the redraw loop
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            _output = new WallOutputService(_clock);

            BuildWallGrid();
            BuildVirtualWallGrid();
            BuildEffectButtons();

            ApplyControlsToEngine();
            ApplyFaultSettings();
            UpdateControlLabels();
            UpdateStatusText();

            RefreshSerialPorts();

            // Let the engine see the music. From here, any effect that wants to
            // react to audio can simply read it from its EffectContext.
            _clock.AudioSource = _audio;

            _clock.Start();

            // Attach the virtual wall straight away, so the whole output
            // pipeline is exercised from the moment the app opens. When real
            // serial arrives it will be attached the same way, in place of this.
            _output.Attach(_loopback);

            RenderWall();
            RenderVirtualWall();
            UpdateOutputStatsText();

            StartRenderLoop();

            // Shut the background threads down cleanly, and let the output
            // service send its blackout packet, before the window goes away.
            Closed += (_, _) =>
            {
                _audio.Dispose();
                _output.Dispose();
                _clock.Dispose();
            };
        }

        /// <summary>
        /// Makes a solid colour brush and freezes it so it can never change.
        ///
        /// Frozen brushes are safe to share everywhere and are cheaper for WPF
        /// to draw with, because it knows they will never need re-checking.
        /// </summary>
        private static Brush CreateFrozenBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Starts the repeating redraw.
        ///
        /// WHY CompositionTarget.Rendering RATHER THAN A TIMER
        ///
        /// The obvious approach is a DispatcherTimer set to fire every 16
        /// milliseconds for 60 frames a second. That was tried first and only
        /// managed about 37.
        ///
        /// The reason is that Windows timers have a granularity of roughly 15.6
        /// milliseconds by default. A timer asked for 16.7 cannot be given it -
        /// it gets 15.6 or 31.2, and averages out somewhere well short of the
        /// target.
        ///
        /// CompositionTarget.Rendering instead fires once for every frame WPF
        /// itself draws, in step with the screen's own refresh. That gives a
        /// smooth 60 a second on a typical monitor, with no timer granularity
        /// involved at all.
        ///
        /// It also puts the redraw at exactly the right moment in WPF's cycle:
        /// just before the frame is composed, so changes made here appear in
        /// that same frame rather than waiting for the next one.
        /// </summary>
        private void StartRenderLoop()
        {
            _frameClock.Start();

            CompositionTarget.Rendering += OnRendering;

            // Detach when the window closes. CompositionTarget.Rendering is a
            // static event, so a handler left attached would keep this window
            // alive in memory after it had been closed.
            Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
        }

        /// <summary>
        /// Creates the 35 clickable buttons representing the bulbs.
        ///
        /// Building them in code rather than writing 35 button definitions in
        /// XAML keeps the layout file readable and means the wall size is
        /// defined in exactly one place - the constants on WallFrame.
        /// </summary>
        private void BuildWallGrid()
        {
            WallGrid.Children.Clear();

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    var button = new Button
                    {
                        Margin = new Thickness(8),
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,

                        // Remember which cell this button represents so the
                        // click handler knows what was pressed.
                        Tag = new CellPosition(row, column)
                    };

                    button.Click += CellButton_Click;

                    WallGrid.Children.Add(button);

                    // Also keep a direct reference for fast redrawing.
                    _cellButtons[GetCellIndex(row, column)] = button;
                }
            }
        }

        /// <summary>
        /// Creates the 35 coloured squares of the virtual wall display.
        ///
        /// Plain Borders rather than Buttons: nothing here is clickable, and
        /// button hover highlighting would be actively misleading on a display
        /// whose only job is to report what the hardware would be showing.
        /// </summary>
        private void BuildVirtualWallGrid()
        {
            VirtualWallGrid.Children.Clear();

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    var cell = new Border
                    {
                        Margin = new Thickness(8),
                        CornerRadius = new CornerRadius(4),
                        BorderThickness = new Thickness(2)
                    };

                    VirtualWallGrid.Children.Add(cell);
                    _virtualCells[GetCellIndex(row, column)] = cell;
                }
            }
        }

        /// <summary>
        /// Creates one button per effect, in the three labelled sections.
        ///
        /// This is where the catalog pays off: the window does not know or care
        /// what effects exist. It asks, and builds buttons for whatever comes
        /// back. Adding a new effect to EffectCatalog makes it appear here with
        /// no changes to this file.
        /// </summary>
        private void BuildEffectButtons()
        {
            // Static patterns get smaller buttons because there are more of them
            // and their names are short. 96 is chosen so all nine fit on one
            // row at the window's default width - at 110 they wrapped onto a
            // second row and pushed the wall itself down the window.
            AddEffectButtons(StaticPatternsPanel, _catalog.StaticPatterns, width: 96, height: 32);

            AddEffectButtons(SequenceAnimationsPanel, _catalog.SequenceAnimations, width: 150, height: 36);
            AddEffectButtons(ProceduralAnimationsPanel, _catalog.ProceduralAnimations, width: 150, height: 36);
        }

        /// <summary>
        /// Adds a button for each effect in a list to one of the panels.
        /// </summary>
        private void AddEffectButtons(
            System.Windows.Controls.Panel panel,
            IReadOnlyList<IWallEffect> effects,
            double width,
            double height)
        {
            panel.Children.Clear();

            foreach (IWallEffect effect in effects)
            {
                var button = new Button
                {
                    Content = effect.DisplayName,
                    Width = width,
                    Height = height,
                    Margin = new Thickness(0, 0, 10, 10),

                    // The description written on the effect becomes its tooltip,
                    // so explanations live with the effect rather than being
                    // duplicated in the layout file.
                    ToolTip = effect.Description,

                    // Carry the effect itself on the button, so the shared click
                    // handler knows which one to play.
                    Tag = effect
                };

                button.Click += EffectButton_Click;

                panel.Children.Add(button);
                _effectButtons[effect] = button;
            }
        }

        /// <summary>
        /// Converts a row and column into a position in the flat button and
        /// state arrays. Row-major, matching the serializer's bit numbering.
        /// </summary>
        private static int GetCellIndex(int row, int column)
        {
            return (row * WallFrame.Columns) + column;
        }

        /// <summary>
        /// Runs once per drawn frame: redraws the wall from the clock's state.
        ///
        /// Note what this no longer does. It does not advance the engine. The
        /// show clock does that on its own thread, at its own rate, whether the
        /// window is drawing or not.
        ///
        /// This method's only job now is to display. That separation is what
        /// lets the wall be driven at 30 packets a second while the screen
        /// refreshes at 60, and what stops a busy or stalled interface from
        /// disturbing the wall's timing.
        /// </summary>
        private void OnRendering(object? sender, EventArgs e)
        {
            // Read the elapsed time and immediately restart the stopwatch, so
            // the next frame measures from this moment. Still needed, but only
            // for the frame-rate readout now.
            double deltaSeconds = _frameClock.Elapsed.TotalSeconds;
            _frameClock.Restart();

            RenderWall();
            RenderVirtualWall();

            // The meter is refreshed every frame rather than on the slower
            // statistics schedule. A level meter that updates four times a
            // second reads as broken; this needs to look continuous.
            UpdateAudioReadout(deltaSeconds);

            UpdateFrameRateReadout(deltaSeconds);
            UpdateOutputStatsPeriodically(deltaSeconds);
        }

        /// <summary>
        /// Makes the on-screen wall match the clock's current frame.
        ///
        /// Only cells that actually changed are touched. Asking WPF to restyle a
        /// button causes it to redraw that button, so restyling all 35 every
        /// frame would mean 35 redraws sixty times a second when typically only
        /// a few bulbs changed.
        /// </summary>
        private void RenderWall()
        {
            // Take a private copy first. The clock's own frame is being
            // rewritten by another thread while we work, so reading it directly
            // could give us half of one frame and half of the next.
            _clock.CopyCurrentFrameTo(_displayFrame);

            WallFrame frame = _displayFrame;
            bool anythingChanged = false;

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    int index = GetCellIndex(row, column);
                    bool isOn = frame.GetCell(row, column);

                    // Skip cells that look the same as last time.
                    if (!_forceFullRedraw && _renderedCellStates[index] == isOn)
                    {
                        continue;
                    }

                    _renderedCellStates[index] = isOn;
                    ApplyCellAppearance(_cellButtons[index], isOn);
                    anythingChanged = true;
                }
            }

            // The packet preview only needs rebuilding when the wall actually
            // changed. Rebuilding those strings sixty times a second for a
            // picture that is standing still would be wasted work.
            if (anythingChanged || _forceFullRedraw)
            {
                UpdateSerializedPacketPreview();
            }

            _forceFullRedraw = false;
        }

        /// <summary>
        /// Draws what the virtual wall - the software model of the Arduino - is
        /// currently showing.
        ///
        /// WHAT THIS IS FOR
        ///
        /// Comparison. The wall above it shows what the engine decided; this one
        /// shows what a real wall would actually be displaying, having received
        /// only the packets that survived the journey.
        ///
        /// While everything is working the two are identical, which is the proof
        /// that packing, transmission, framing, checksum validation and
        /// unpacking all agree with each other.
        ///
        /// Turn up the fault sliders and this one starts falling behind, holding
        /// an older frame while damaged packets are discarded, then snapping
        /// back into step when a good one arrives. That is the genuine recovery
        /// behaviour, running for real.
        /// </summary>
        private void RenderVirtualWall()
        {
            // Take a copy: the output thread is writing into the receiver while
            // we read, so reading it directly could give a torn picture.
            _loopback.CopyReceivedFrameTo(_virtualFrame);

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    int index = GetCellIndex(row, column);
                    bool isOn = _virtualFrame.GetCell(row, column);

                    if (!_forceVirtualRedraw && _renderedVirtualStates[index] == isOn)
                    {
                        continue;
                    }

                    _renderedVirtualStates[index] = isOn;

                    Border cell = _virtualCells[index];
                    cell.Background = isOn ? LitBackgroundBrush : UnlitBackgroundBrush;
                    cell.BorderBrush = isOn ? LitBorderBrush : UnlitBorderBrush;
                }
            }

            _forceVirtualRedraw = false;
        }

        /// <summary>
        /// Sets one button's colours and label to match a bulb being on or off.
        /// </summary>
        private static void ApplyCellAppearance(Button button, bool isOn)
        {
            button.Content = isOn ? "ON" : "OFF";
            button.Background = isOn ? LitBackgroundBrush : UnlitBackgroundBrush;
            button.Foreground = isOn ? Brushes.Black : Brushes.White;
            button.BorderBrush = isOn ? LitBorderBrush : UnlitBorderBrush;
        }

        /// <summary>
        /// Updates the measured redraw-rate readout.
        ///
        /// This reports what is really happening rather than what was intended,
        /// which is the useful version. On a typical 60 Hz monitor it should sit
        /// near 60; noticeably below that means something is making the
        /// interface struggle.
        /// </summary>
        private void UpdateFrameRateReadout(double deltaSeconds)
        {
            _framesSinceRateUpdate++;
            _secondsSinceRateUpdate += deltaSeconds;

            if (_secondsSinceRateUpdate < FrameRateUpdateIntervalSeconds)
            {
                return;
            }

            double framesPerSecond = _framesSinceRateUpdate / _secondsSinceRateUpdate;
            FrameRateTextBlock.Text = $"Simulator: {framesPerSecond:F0} fps";

            _framesSinceRateUpdate = 0;
            _secondsSinceRateUpdate = 0.0;
        }

        /// <summary>
        /// Shows the bytes being sent to the wall for the frame on screen.
        /// </summary>
        private void UpdateSerializedPacketPreview()
        {
            byte[] payload = WallFrameSerializer.SerializeFrameData(_displayFrame);
            byte[] packet = WallFrameSerializer.CreateFramePacket(_displayFrame);

            SerializedPacketTextBox.Text =
                $"Payload (5 bytes): {WallFrameSerializer.ToHexString(payload)}{Environment.NewLine}" +
                $"Packet  (9 bytes): {WallFrameSerializer.ToHexString(packet)}{Environment.NewLine}" +
                $"Bulbs lit: {_displayFrame.CountLitCells()} of {WallFrame.Rows * WallFrame.Columns}";
        }

        /// <summary>
        /// Refreshes the output statistics every so often.
        ///
        /// Not on every drawn frame, because the numbers would change too fast
        /// to read and rebuilding the text sixty times a second is wasteful.
        /// </summary>
        private void UpdateOutputStatsPeriodically(double deltaSeconds)
        {
            _secondsSinceOutputStatsUpdate += deltaSeconds;

            if (_secondsSinceOutputStatsUpdate < OutputStatsUpdateIntervalSeconds)
            {
                return;
            }

            _secondsSinceOutputStatsUpdate = 0.0;

            // Let the virtual wall's watchdog run even during a stretch when
            // nothing is being sent, so that stopping output visibly blanks it
            // after the timeout - exactly as the real wall would behave.
            _loopback.UpdateWatchdog();

            UpdateOutputStatsText();
            UpdateSerialStatusText();
        }

        /// <summary>
        /// Writes the current state of the output pipeline into the readout.
        ///
        /// This is the window onto everything happening downstream of the
        /// engine. Until real hardware is connected it is the main evidence
        /// that the pipeline works: packets going out at the expected rate,
        /// arriving intact, and being decoded into the wall we expect.
        /// </summary>
        private void UpdateOutputStatsText()
        {
            string transportName = _output.Transport?.Name ?? "not connected";

            OutputStatusTextBlock.Text = $"Output: {transportName}";

            OutputStatsTextBlock.Text =
                $"Engine {_clock.MeasuredTicksPerSecond:F0} Hz   " +
                $"Sending {_output.MeasuredPacketsPerSecond:F0} pkt/s   " +
                $"Sent {_output.PacketsSent}{Environment.NewLine}" +
                $"Virtual wall: {_loopback.ValidPacketsReceived} ok, " +
                $"{_loopback.ChecksumFailures} bad checksum, " +
                $"{_loopback.BytesDiscarded} bytes discarded" +
                (_loopback.WatchdogTripped ? "   [WATCHDOG TRIPPED]" : string.Empty);
        }

        /// <summary>
        /// Copies every slider's current value into the engine.
        ///
        /// Called once at startup so the engine begins in step with whatever the
        /// sliders were left at in the layout file.
        ///
        /// Everything goes through _clock.Modify, which takes the lock before
        /// touching the engine. The engine is being ticked by another thread, so
        /// writing to it directly from here would be a race - two threads
        /// changing the same values at the same time, producing bugs that appear
        /// at random and cannot be reproduced on demand.
        /// </summary>
        private void ApplyControlsToEngine()
        {
            _clock.Modify(engine =>
            {
                // The slider reads as a percentage; the engine wants a
                // multiplier, where 1.0 means normal speed. 150% becomes 1.5.
                engine.SpeedMultiplier = SpeedSlider.Value / 100.0;

                // Center Y shifts up and down, which is a row offset.
                // Center X shifts left and right, which is a column offset.
                engine.OffsetRows = (int)CenterYSlider.Value;
                engine.OffsetColumns = (int)CenterXSlider.Value;

                engine.Parameters.MeteorTailLength = (int)MeteorTailLengthSlider.Value;
            });
        }

        /// <summary>
        /// Starts listening to the computer's audio output.
        /// </summary>
        private void AudioStartButton_Click(object sender, RoutedEventArgs e)
        {
            _audio.Start();

            AudioStartButton.IsEnabled = !_audio.IsRunning;
            AudioStopButton.IsEnabled = _audio.IsRunning;

            // No time has passed since the last redraw as far as the meters are
            // concerned, so nothing should decay on this call.
            UpdateAudioReadout(0.0);
        }

        /// <summary>
        /// Stops listening.
        /// </summary>
        private void AudioStopButton_Click(object sender, RoutedEventArgs e)
        {
            _audio.Stop();

            AudioStartButton.IsEnabled = true;
            AudioStopButton.IsEnabled = false;

            UpdateAudioReadout(0.0);
        }

        /// <summary>
        /// Redraws the level meters and the audio status line.
        ///
        /// Called on every drawn frame, because a meter that updates a few times
        /// a second looks broken rather than smooth. The work is trivial - a few
        /// widths and a short string.
        /// </summary>
        /// <param name="deltaSeconds">
        /// How long since the previous redraw. Only the trigger meter uses it,
        /// to fall back at the same pace whatever the frame rate happens to be.
        /// </param>
        private void UpdateAudioReadout(double deltaSeconds)
        {
            // Let the level decay when nothing is playing. Windows stops sending
            // buffers entirely during silence rather than sending zeros, so
            // without this nudge the meter would freeze wherever the music left
            // it.
            _audio.UpdateIdle();

            AudioFeatures features = _audio.CurrentFeatures;

            // The top bar shows the value that actually drives the wall, so what
            // the meter does and what the bulbs do should always agree.
            SetBarWidth(AudioLevelBar, features.NormalisedLevel);
            SetBarWidth(AudioPeakBar, features.Peak);

            UpdateBeatMeters(features, deltaSeconds);

            if (_audio.LastError is not null)
            {
                AudioStatusTextBlock.Text = _audio.LastError;
                return;
            }

            if (!_audio.IsRunning)
            {
                AudioStatusTextBlock.Text = "Not listening";
                return;
            }

            // The tempo readout. Confidence sits beside it deliberately: a
            // confident wrong answer and an unconfident one look identical
            // without it, and knowing which you have changes what to do.
            string tempo = features.TempoBpm > 0.0
                ? $"{features.TempoBpm:F0} BPM ({features.TempoConfidence:P0} sure)"
                : "BPM: listening...";

            AudioStatusTextBlock.Text =
                $"Listening to {_audio.Name}{Environment.NewLine}" +
                $"drives wall {features.NormalisedLevel:F2}   " +
                $"raw level {features.Level:F2}   " +
                $"auto-gain ref {_audio.GainReference:F2}" +
                (features.IsSilent ? "   [silent]" : string.Empty) +
                $"{Environment.NewLine}{tempo}   beats {features.BeatCount}";
        }

        /// <summary>
        /// Where along the trigger meter the red line sits.
        ///
        /// This has to match the column widths given to the meter in the XAML,
        /// which are 2 parts to 3 - so two fifths of the way across. Changing
        /// one without the other would put the line somewhere the bar does not
        /// agree with, and the meter would quietly lie.
        ///
        /// The alternative was to position the line from code as well, which
        /// removes the duplication but adds a value that has to be recalculated
        /// every time the window is resized. Two numbers that must match, in
        /// files that sit next to each other, seemed the smaller problem.
        /// </summary>
        private const double TriggerPointFraction = 0.4;

        /// <summary>
        /// The reading a completely full trigger meter stands for.
        ///
        /// The red line is at 1.0 and sits two fifths of the way across, so the
        /// remaining three fifths carry up to 2.5.
        /// </summary>
        private const double TriggerMeterTop = 1.0 / TriggerPointFraction;

        /// <summary>
        /// How fast the trigger meter falls back, in meter-lengths per second.
        ///
        /// Chosen so a full bar empties in a bit under half a second. Slower and
        /// fast beats would smear into one another; faster and a spike would be
        /// gone before the eye caught it.
        /// </summary>
        private const double TriggerFallPerSecond = 6.0;

        /// <summary>
        /// How long the beat lamp stays lit after a beat, in seconds.
        ///
        /// The screen redraws about every 17 ms, so anything much shorter than
        /// this would be one or two frames and easy to miss entirely.
        ///
        /// The trade: wound all the way down, the beat gap slider allows beats
        /// 0.05 s apart, and at that setting the lamp would never go out between
        /// them. That is accepted deliberately - the bottom of that slider is an
        /// extreme setting for diagnosing double-triggering, and the meter is
        /// the thing to watch for that. At any normal tempo this reads as a
        /// clear, separate blink.
        /// </summary>
        private const double BeatLampSeconds = 0.08;

        /// <summary>
        /// Redraws the trigger meter and the beat lamp.
        /// </summary>
        private void UpdateBeatMeters(AudioFeatures features, double deltaSeconds)
        {
            if (!_audio.IsRunning)
            {
                // Nothing is being listened to, so the meter has nothing to
                // report. Emptying it outright rather than letting it decay
                // makes "stopped" look different from "playing something quiet".
                _displayedTriggerRatio = 0.0;
                SetBarWidth(BeatTriggerBar, 0.0);
                BeatLamp.Background = BeatLampUnlitBrush;
                return;
            }

            // Anything past the top of the meter looks the same on screen, so
            // there is nothing to gain by remembering how far past it went.
            //
            // WHY THIS CLAMP IS HERE, AND WHAT HAPPENED WITHOUT IT
            //
            // The first version stored the reading as it came. That seemed
            // harmless - the bar is clamped when it is drawn, so what could it
            // matter? It mattered a lot.
            //
            // A hit landing after a quiet moment is measured against a very low
            // threshold, so the ratio is not 2 or 3 but sometimes 20 or more.
            // Storing 20 and then draining it at 6 a second means twenty of
            // those units have to be worked through before the bar so much as
            // twitches - well over three seconds, by which time the next several
            // beats have topped it up again.
            //
            // The result was a meter that pinned at full the moment music
            // started and simply stayed there, which was spotted only by playing
            // something and watching it. It looked like a plausible reading, and
            // that is what made it worth catching: the meter is supposed to be
            // the thing you trust while tuning.
            double latest = Math.Min(_audio.BeatTriggerRatio, TriggerMeterTop);

            // Rise instantly, fall gradually. See _displayedTriggerRatio for why
            // the meter needs to remember anything at all.
            if (latest >= _displayedTriggerRatio)
            {
                _displayedTriggerRatio = latest;
            }
            else
            {
                _displayedTriggerRatio = Math.Max(
                    latest,
                    _displayedTriggerRatio - (TriggerFallPerSecond * deltaSeconds));
            }

            // A reading of 1 has to land on the red line, so the fraction of the
            // track to fill is the reading times where that line sits.
            SetBarWidth(BeatTriggerBar, _displayedTriggerRatio * TriggerPointFraction);

            BeatLamp.Background = features.SecondsSinceBeat <= BeatLampSeconds
                ? BeatLampLitBrush
                : BeatLampUnlitBrush;
        }

        /// <summary>
        /// Runs whenever the beat size slider moves.
        ///
        /// Takes effect on the very next audio buffer, so the change can be
        /// judged by ear while still dragging - which is the entire point of
        /// having it on a slider rather than in the code.
        /// </summary>
        private void BeatSensitivitySlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            // WPF raises this while the window is still being built, before the
            // named elements exist.
            if (BeatSensitivityValueTextBlock is null)
            {
                return;
            }

            _audio.BeatSensitivity = BeatSensitivitySlider.Value;
            BeatSensitivityValueTextBlock.Text = $"{BeatSensitivitySlider.Value:F2}x";
        }

        /// <summary>
        /// Runs whenever the beat gap slider moves.
        /// </summary>
        private void BeatGapSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (BeatGapValueTextBlock is null)
            {
                return;
            }

            _audio.MinimumSecondsBetweenBeats = BeatGapSlider.Value;
            BeatGapValueTextBlock.Text = $"{BeatGapSlider.Value:F2}s";
        }

        /// <summary>
        /// Runs whenever the sensitivity slider moves.
        ///
        /// Takes effect on the very next audio buffer, so the wall responds
        /// while the slider is being dragged.
        /// </summary>
        private void AudioSensitivitySlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            // WPF raises this while the window is still being built, before the
            // named elements exist.
            if (AudioSensitivityValueTextBlock is null)
            {
                return;
            }

            _audio.Sensitivity = AudioSensitivitySlider.Value;
            AudioSensitivityValueTextBlock.Text = $"{AudioSensitivitySlider.Value:F1}x";
        }

        /// <summary>
        /// Runs whenever the smoothing slider moves.
        ///
        /// Takes effect on the very next audio buffer, so the difference between
        /// twitchy and flowing can be heard and seen while dragging.
        /// </summary>
        private void AudioSmoothingSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (AudioSmoothingValueTextBlock is null)
            {
                return;
            }

            _audio.Smoothing = AudioSmoothingSlider.Value;
            AudioSmoothingValueTextBlock.Text = $"{AudioSmoothingSlider.Value:F2}";
        }

        /// <summary>
        /// Sets a meter bar to a fraction of the space available to it.
        ///
        /// The width is worked out from the parent's measured size rather than
        /// from a fixed number, so the meter follows the window as it is
        /// resized.
        /// </summary>
        private static void SetBarWidth(Rectangle bar, double fraction)
        {
            if (bar.Parent is not FrameworkElement track)
            {
                return;
            }

            double available = track.ActualWidth;

            // Before the window has been laid out, the parent has no measured
            // size yet and this would produce nonsense.
            if (double.IsNaN(available) || available <= 0.0)
            {
                return;
            }

            bar.Width = Math.Clamp(fraction, 0.0, 1.0) * available;
        }

        /// <summary>
        /// Re-reads the list of serial ports.
        ///
        /// Needed because ports appear and disappear as things are plugged in.
        /// The Arduino will not be listed until its cable is connected.
        /// </summary>
        private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshSerialPorts();
        }

        /// <summary>
        /// Fills the port dropdown, keeping the current selection if it is still
        /// present.
        /// </summary>
        private void RefreshSerialPorts()
        {
            string? previouslySelected = SerialPortComboBox.SelectedItem as string;

            string[] ports = SerialPortLister.GetAvailablePortNames();

            SerialPortComboBox.ItemsSource = ports;

            if (previouslySelected is not null && ports.Contains(previouslySelected))
            {
                SerialPortComboBox.SelectedItem = previouslySelected;
            }
            else if (ports.Length > 0)
            {
                // Default to the highest-numbered port, because a freshly
                // plugged-in Arduino usually takes the next free number and so
                // tends to be last in the list.
                SerialPortComboBox.SelectedItem = ports[^1];
            }
        }

        /// <summary>
        /// Opens the selected port and starts driving the real wall.
        ///
        /// The virtual wall is kept running alongside it rather than replaced,
        /// which is what makes it possible to tell an app problem from a
        /// hardware problem at a glance.
        /// </summary>
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (SerialPortComboBox.SelectedItem is not string portName)
            {
                SerialStatusTextBlock.Text = "Choose a port first. Press Refresh if the list is empty.";
                return;
            }

            try
            {
                var serial = new SerialTransport(portName);

                // Both transports, so both walls stay live.
                _output.Attach(new CompositeTransport(_loopback, serial));

                _serial = serial;

                ConnectButton.IsEnabled = false;
                DisconnectButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                // The usual causes are that the port has vanished, or that
                // something else already has it open — very often the Arduino
                // IDE's serial monitor, which is worth checking first.
                _serial = null;
                SerialStatusTextBlock.Text =
                    $"Could not open {portName}.{Environment.NewLine}{ex.Message}" +
                    $"{Environment.NewLine}If the Arduino IDE's serial monitor is open, close it and try again.";

                // Fall back to the virtual wall alone, so the app keeps working.
                _output.Attach(_loopback);
            }

            UpdateSerialStatusText();
        }

        /// <summary>
        /// Closes the port and returns to driving the virtual wall alone.
        ///
        /// The output service sends a blackout before disconnecting, so the real
        /// wall goes dark rather than being left frozen on whatever frame
        /// happened to be showing.
        /// </summary>
        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            _output.Attach(_loopback);
            _serial = null;

            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;

            UpdateSerialStatusText();
        }

        /// <summary>
        /// Describes the state of the serial connection.
        ///
        /// The "waiting for board" state is the one that earns its keep. Opening
        /// a port resets the Arduino, and for the couple of seconds afterwards
        /// its bootloader ignores everything sent. Without this line, those
        /// seconds look exactly like a dead connection, and the natural reaction
        /// is to start debugging something that is working fine.
        /// </summary>
        private void UpdateSerialStatusText()
        {
            if (_serial is null)
            {
                SerialStatusTextBlock.Text = "Not connected — virtual wall only";
                return;
            }

            if (_serial.LastError is not null)
            {
                SerialStatusTextBlock.Text = _serial.LastError;
                return;
            }

            if (!_serial.IsConnected)
            {
                SerialStatusTextBlock.Text = $"{_serial.PortName} closed";
                return;
            }

            if (_serial.IsWaitingForBoardReset)
            {
                SerialStatusTextBlock.Text =
                    $"{_serial.PortName} open — waiting for the board to restart..." +
                    $"{Environment.NewLine}(opening the port resets the Arduino; this is normal)";
                return;
            }

            SerialStatusTextBlock.Text =
                $"{_serial.PortName} connected at {_serial.BaudRate} baud{Environment.NewLine}" +
                $"{_serial.PacketsWritten} packets sent, " +
                $"{_serial.PacketsDroppedDuringReset} dropped while the board restarted";
        }

        /// <summary>
        /// Starts the hardware check, lighting the first bulb.
        /// </summary>
        private void IdentifyStartButton_Click(object sender, RoutedEventArgs e)
        {
            IWallEffect? identify = _catalog.FindByName("Identify Bulb");

            if (identify is null)
            {
                return;
            }

            _clock.Modify(engine =>
            {
                engine.Parameters.IdentifyBulbIndex = 0;
                engine.Play(identify);
            });

            UpdateStatusText();
            UpdateIdentifyReadout();
        }

        /// <summary>
        /// Steps back one bulb.
        /// </summary>
        private void IdentifyPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            StepIdentifyBy(-1);
        }

        /// <summary>
        /// Steps forward one bulb.
        /// </summary>
        private void IdentifyNextButton_Click(object sender, RoutedEventArgs e)
        {
            StepIdentifyBy(1);
        }

        /// <summary>
        /// Moves the identified bulb forward or back, wrapping round at both
        /// ends so stepping past bulb 34 returns to bulb 0.
        ///
        /// Wrapping rather than stopping because this gets used while walking
        /// round a wall, and hitting an invisible wall at one end is a small
        /// annoyance that would happen dozens of times in a session.
        /// </summary>
        private void StepIdentifyBy(int delta)
        {
            _clock.Modify(engine =>
            {
                int count = WallHardwareMap.BulbCount;

                // Adding count before taking the remainder keeps the result
                // positive. In C#, -1 % 35 is -1 rather than 34, which would
                // put us outside the wall.
                int next = ((engine.Parameters.IdentifyBulbIndex + delta) % count + count) % count;

                engine.Parameters.IdentifyBulbIndex = next;
            });

            EnsureIdentifyEffectRunning();
            UpdateIdentifyReadout();
        }

        /// <summary>
        /// Jumps to whatever relay label has been typed, such as "C4".
        /// </summary>
        private void IdentifyGoButton_Click(object sender, RoutedEventArgs e)
        {
            GoToTypedRelayLabel();
        }

        /// <summary>
        /// Lets Enter work in the label box, so a label can be typed and
        /// confirmed without reaching for the mouse.
        /// </summary>
        private void IdentifyLabelTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                GoToTypedRelayLabel();
            }
        }

        /// <summary>
        /// Reads the typed relay label and lights that bulb.
        ///
        /// A label that cannot be understood says so in the readout rather than
        /// failing silently, because a typo while standing at the wall should be
        /// obvious immediately rather than looking like a dead bulb.
        /// </summary>
        private void GoToTypedRelayLabel()
        {
            string typed = IdentifyLabelTextBox.Text;

            if (!WallHardwareMap.TryParseRelayLabel(typed, out int bulbIndex))
            {
                IdentifyReadoutTextBlock.Text =
                    $"Could not read \"{typed}\" as a relay label. Expected something like A1 or E7.";
                return;
            }

            _clock.Modify(engine => engine.Parameters.IdentifyBulbIndex = bulbIndex);

            EnsureIdentifyEffectRunning();
            UpdateIdentifyReadout();
        }

        /// <summary>
        /// Switches to the identify effect if something else is playing.
        ///
        /// This means the Previous, Next and Go controls work straight away
        /// without having to press Start first, which is one less thing to
        /// remember while concentrating on the wall.
        /// </summary>
        private void EnsureIdentifyEffectRunning()
        {
            IWallEffect? identify = _catalog.FindByName("Identify Bulb");

            if (identify is null || ReferenceEquals(_clock.ActiveEffect, identify))
            {
                return;
            }

            _clock.Modify(engine => engine.Play(identify));
            UpdateStatusText();
        }

        /// <summary>
        /// Writes the current bulb's four names into the readout.
        /// </summary>
        private void UpdateIdentifyReadout()
        {
            int bulbIndex = 0;
            _clock.Modify(engine => bulbIndex = engine.Parameters.IdentifyBulbIndex);

            IdentifyReadoutTextBlock.Text = WallHardwareMap.Describe(bulbIndex);
        }

        /// <summary>
        /// Copies the fault sliders into the loopback transport.
        ///
        /// The sliders read as a percentage; the transport wants a probability
        /// from 0 to 1, so 2.5% becomes 0.025.
        /// </summary>
        private void ApplyFaultSettings()
        {
            _loopback.ByteDropProbability = ByteDropSlider.Value / 100.0;
            _loopback.ByteCorruptionProbability = ByteCorruptSlider.Value / 100.0;
        }

        /// <summary>
        /// Refreshes the small number shown beside each slider.
        /// </summary>
        private void UpdateControlLabels()
        {
            SpeedValueTextBlock.Text = $"{(int)SpeedSlider.Value}%";
            CenterXValueTextBlock.Text = ((int)CenterXSlider.Value).ToString();
            CenterYValueTextBlock.Text = ((int)CenterYSlider.Value).ToString();
            MeteorTailLengthValueTextBlock.Text = ((int)MeteorTailLengthSlider.Value).ToString();

            ByteDropValueTextBlock.Text = $"{ByteDropSlider.Value:F1}%";
            ByteCorruptValueTextBlock.Text = $"{ByteCorruptSlider.Value:F1}%";
        }

        /// <summary>
        /// Runs whenever either fault slider moves.
        ///
        /// Takes effect on the very next packet, so the virtual wall starts
        /// misbehaving - and recovering - while you watch.
        /// </summary>
        private void FaultSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // WPF raises this while the window is still being built, before the
            // named elements exist.
            if (ByteDropValueTextBlock is null || ByteCorruptValueTextBlock is null)
            {
                return;
            }

            ApplyFaultSettings();
            UpdateControlLabels();
        }

        /// <summary>
        /// Updates the status readout and highlights whichever effect button is
        /// currently playing.
        /// </summary>
        private void UpdateStatusText()
        {
            IWallEffect? active = _clock.ActiveEffect;

            if (active is null)
            {
                AnimationStatusTextBlock.Text = "Manual mode";
                EffectDescriptionTextBlock.Text = "Click cells to toggle bulbs by hand.";
            }
            else
            {
                AnimationStatusTextBlock.Text = $"Playing: {active.DisplayName}";
                EffectDescriptionTextBlock.Text = active.Description;
            }

            // Repaint every effect button so exactly one appears selected.
            foreach (KeyValuePair<IWallEffect, Button> pair in _effectButtons)
            {
                bool isActive = ReferenceEquals(pair.Key, active);
                pair.Value.Background = isActive ? ActiveEffectBrush : InactiveEffectBrush;
                pair.Value.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
            }
        }

        /// <summary>
        /// Handles a click on any of the generated effect buttons.
        ///
        /// One handler serves all of them. Which effect to play is read from the
        /// button's Tag, which was set when the button was created.
        ///
        /// This replaces the twelve near-identical handlers this file used to
        /// have, one per effect.
        /// </summary>
        private void EffectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not IWallEffect effect)
            {
                return;
            }

            _clock.Modify(engine => engine.Play(effect));

            UpdateStatusText();
            RenderWall();
        }

        /// <summary>
        /// Stops the running effect, leaving the current frame frozen on screen.
        /// </summary>
        private void StopAnimationButton_Click(object sender, RoutedEventArgs e)
        {
            _clock.Modify(engine => engine.Stop());
            UpdateStatusText();
        }

        /// <summary>
        /// Handles clicks on individual bulbs in the simulator.
        ///
        /// The engine switches itself into manual mode when a cell is toggled,
        /// because an effect would otherwise paint over the change within a
        /// fraction of a second.
        /// </summary>
        private void CellButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not CellPosition position)
            {
                return;
            }

            _clock.Modify(engine => engine.ToggleCell(position.Row, position.Column));

            UpdateStatusText();
            RenderWall();
        }

        /// <summary>
        /// Runs whenever the speed slider moves.
        ///
        /// The change applies immediately, mid-animation. Because speed now
        /// scales how quickly effect time accumulates rather than restarting
        /// anything, the animation simply carries on from where it was at the
        /// new pace.
        /// </summary>
        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // WPF raises this while the window is still being built, before the
            // named elements exist. Nothing useful can be done that early, so
            // check and return.
            if (SpeedValueTextBlock is null)
            {
                return;
            }

            _clock.Modify(engine => engine.SpeedMultiplier = SpeedSlider.Value / 100.0);
            UpdateControlLabels();
        }

        /// <summary>
        /// Runs whenever either Center slider moves.
        ///
        /// These shift the frame data itself rather than merely shifting the
        /// picture on screen, so the simulator keeps showing exactly what would
        /// be sent to the hardware.
        ///
        /// Unlike before, this now affects static patterns as well as
        /// animations. Static patterns are effects too now, and are redrawn on
        /// every tick like everything else, so they follow the offsets in the
        /// same way. That inconsistency is gone.
        /// </summary>
        private void CenterSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (CenterXValueTextBlock is null || CenterYValueTextBlock is null)
            {
                return;
            }

            _clock.Modify(engine =>
            {
                engine.OffsetRows = (int)CenterYSlider.Value;
                engine.OffsetColumns = (int)CenterXSlider.Value;
            });

            UpdateControlLabels();
        }

        /// <summary>
        /// Runs whenever the meteor tail slider moves.
        ///
        /// The engine reads this value fresh on every frame, so dragging the
        /// slider changes the tail while the meteor is mid-flight.
        /// </summary>
        private void MeteorTailLengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MeteorTailLengthValueTextBlock is null)
            {
                return;
            }

            _clock.Modify(engine =>
                engine.Parameters.MeteorTailLength = (int)MeteorTailLengthSlider.Value);
            UpdateControlLabels();
        }

        /// <summary>
        /// Small helper type recording which bulb a wall button represents.
        ///
        /// Every wall button carries one of these in its Tag property, so the
        /// shared click handler can tell which of the 35 was pressed.
        ///
        /// "record" is a compact way to declare a small type that exists purely
        /// to hold a couple of values together.
        /// </summary>
        private record CellPosition(int Row, int Column);
    }
}
