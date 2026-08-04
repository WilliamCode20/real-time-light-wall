using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LightWall.Core.Effects;
using LightWall.Core.Engine;
using LightWall.Core.Models;
using LightWall.Core.Serialization;

namespace LightWall.App
{
    /// <summary>
    /// Code-behind for the main simulator window.
    ///
    /// WHAT THIS CLASS IS RESPONSIBLE FOR NOW
    ///
    /// Only three things:
    ///
    ///   1. building the buttons
    ///   2. passing slider values along to the engine
    ///   3. drawing whatever the engine says the wall looks like
    ///
    /// That is a deliberate reduction. This file used to also decide how
    /// animations played, track which frame was current, apply the offset
    /// sliders, and hold the wall state itself.
    ///
    /// All of that moved into WallEngine, for one practical reason: the serial
    /// layer and the tests both need to know what the wall should look like, and
    /// neither of them can reasonably ask a window. Logic that lives in a window
    /// can only ever be used by that window.
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

        /// <summary>
        /// The engine: the single authority on what the wall should look like.
        /// </summary>
        private readonly WallEngine _engine = new();

        /// <summary>
        /// Every effect the app can play. Used to build the buttons.
        /// </summary>
        private readonly EffectCatalog _catalog = new();

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

        /// <summary>
        /// Constructor for the main window.
        ///
        /// Startup order:
        /// 1. load and connect the XAML
        /// 2. build the 35 wall buttons
        /// 3. build the effect buttons from the catalog
        /// 4. copy the starting slider values into the engine
        /// 5. draw the initial (blank) wall
        /// 6. start the redraw timer
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            BuildWallGrid();
            BuildEffectButtons();

            ApplyControlsToEngine();
            UpdateControlLabels();
            UpdateStatusText();

            RenderWall();

            StartRenderLoop();
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
        /// Runs once per drawn frame: moves the engine forward and redraws.
        ///
        /// The engine is told how much real time actually passed rather than how
        /// much was supposed to pass. If the computer stalls and a frame arrives
        /// late, the animation advances by the correct amount and stays on pace
        /// instead of drifting slowly behind.
        /// </summary>
        private void OnRendering(object? sender, EventArgs e)
        {
            // Read the elapsed time and immediately restart the stopwatch, so
            // the next tick measures from this moment.
            double deltaSeconds = _frameClock.Elapsed.TotalSeconds;
            _frameClock.Restart();

            _engine.Advance(deltaSeconds);

            RenderWall();
            UpdateFrameRateReadout(deltaSeconds);
        }

        /// <summary>
        /// Makes the on-screen wall match the engine's current frame.
        ///
        /// Only cells that actually changed are touched. Asking WPF to restyle a
        /// button causes it to redraw that button, so restyling all 35 every
        /// frame would mean 35 redraws sixty times a second when typically only
        /// a few bulbs changed.
        /// </summary>
        private void RenderWall()
        {
            WallFrame frame = _engine.CurrentFrame;
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
        /// Shows the bytes that would be sent to the Arduino for the frame
        /// currently on screen.
        /// </summary>
        private void UpdateSerializedPacketPreview()
        {
            WallFrame frame = _engine.CurrentFrame;

            byte[] payload = WallFrameSerializer.SerializeFrameData(frame);
            byte[] packet = WallFrameSerializer.CreateFramePacket(frame);

            SerializedPacketTextBox.Text =
                $"Payload (5 bytes): {WallFrameSerializer.ToHexString(payload)}{Environment.NewLine}" +
                $"Packet  (9 bytes): {WallFrameSerializer.ToHexString(packet)}{Environment.NewLine}" +
                $"Bulbs lit: {frame.CountLitCells()} of {WallFrame.Rows * WallFrame.Columns}";
        }

        /// <summary>
        /// Copies every slider's current value into the engine.
        ///
        /// Called once at startup so the engine begins in step with whatever the
        /// sliders were left at in the layout file.
        /// </summary>
        private void ApplyControlsToEngine()
        {
            // The slider reads as a percentage; the engine wants a multiplier,
            // where 1.0 means normal speed. 150% becomes 1.5.
            _engine.SpeedMultiplier = SpeedSlider.Value / 100.0;

            // Center Y shifts up and down, which is a row offset.
            // Center X shifts left and right, which is a column offset.
            _engine.OffsetRows = (int)CenterYSlider.Value;
            _engine.OffsetColumns = (int)CenterXSlider.Value;

            _engine.Parameters.MeteorTailLength = (int)MeteorTailLengthSlider.Value;
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
        }

        /// <summary>
        /// Updates the status readout and highlights whichever effect button is
        /// currently playing.
        /// </summary>
        private void UpdateStatusText()
        {
            IWallEffect? active = _engine.ActiveEffect;

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

            _engine.Play(effect);

            UpdateStatusText();
            RenderWall();
        }

        /// <summary>
        /// Stops the running effect, leaving the current frame frozen on screen.
        /// </summary>
        private void StopAnimationButton_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();
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

            _engine.ToggleCell(position.Row, position.Column);

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

            _engine.SpeedMultiplier = SpeedSlider.Value / 100.0;
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

            _engine.OffsetRows = (int)CenterYSlider.Value;
            _engine.OffsetColumns = (int)CenterXSlider.Value;

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

            _engine.Parameters.MeteorTailLength = (int)MeteorTailLengthSlider.Value;
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
