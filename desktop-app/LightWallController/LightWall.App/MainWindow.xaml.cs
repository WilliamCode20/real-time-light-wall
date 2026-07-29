using LightWall.Core.Models;
using LightWall.Core.Patterns;
using LightWall.Core.Animations;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LightWall.App
{
    /// <summary>
    /// Code-behind for the main simulator window.
    ///
    /// This class is currently acting as the central coordinator for the prototype.
    /// It is responsible for:
    /// - building the clickable 5x7 UI grid
    /// - holding the current active WallFrame
    /// - rendering that frame into the simulator buttons
    /// - responding to button clicks
    /// - starting/stopping simple animations with a timer
    ///
    /// As the project grows, some of this responsibility may move into more
    /// specialized classes, but for the current stage this is a reasonable
    /// beginner-friendly setup.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// The current active wall state being displayed.
        ///
        /// This object is the truth of what the simulator currently shows.
        /// The UI does not store wall state directly; it reads from this model.
        /// </summary>
        private readonly WallFrame _wallFrame = new();

        /// <summary>
        /// Shared random number generator used by random/procedural effects.
        /// </summary>
        private readonly Random _random = new();

        /// <summary>
        /// WPF timer that drives animation playback.
        ///
        /// Each timer tick advances to the next frame or generates the next
        /// procedural frame, then redraws the wall.
        /// </summary>
        private readonly DispatcherTimer _animationTimer = new();

        /// <summary>
        /// Stores a prebuilt list of animation frames when using a fixed
        /// sequence animation like row sweep or border pulse.
        /// </summary>
        private List<WallFrame> _animationFrames = new();

        /// <summary>
        /// Tracks which frame of the current prebuilt animation is active.
        /// </summary>
        private int _animationFrameIndex = 0;

        /// <summary>
        /// Stores the currently active procedural frame generator, if any.
        ///
        /// A procedural generator is a function that takes a step number
        /// and returns a WallFrame for that step.
        ///
        /// If this is null, no procedural animation is active.
        /// </summary>
        private Func<int, WallFrame>? _proceduralFrameGenerator;

        /// <summary>
        /// Tracks the current procedural step number.
        /// Each timer tick usually increments this.
        /// </summary>
        private int _proceduralStep = 0;

        /// <summary>
        /// Stores the "base" interval of the current animation before speed
        /// slider adjustment is applied.
        ///
        /// Example:
        /// - row sweep may want a base interval of 180 ms
        /// - speed slider then modifies that live
        /// </summary>
        private int _baseAnimationIntervalMs = 180;

        /// <summary>
        /// Constructor for the main window.
        ///
        /// Startup flow:
        /// 1. Load and connect XAML
        /// 2. Configure animation timer
        /// 3. Build the 5x7 simulator button grid
        /// 4. Update the speed text
        /// 5. Render the initial wall state
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            ConfigureAnimationTimer();
            BuildWallGrid();
            UpdateSpeedText();
            RenderWall();
        }

        /// <summary>
        /// Sets up the timer used for animation playback.
        ///
        /// Important idea:
        /// We use a DispatcherTimer instead of blocking waits or sleeps because
        /// this keeps the UI responsive while animation plays.
        /// </summary>
        private void ConfigureAnimationTimer()
        {
            _animationTimer.Interval = TimeSpan.FromMilliseconds(_baseAnimationIntervalMs);
            _animationTimer.Tick += AnimationTimer_Tick;
        }

        /// <summary>
        /// Dynamically creates the 35 clickable UI buttons that represent the wall.
        ///
        /// Each button:
        /// - is added to the UniformGrid in XAML
        /// - stores its row/column position in Tag
        /// - uses the same click handler: CellButton_Click
        ///
        /// This means we do NOT need 35 separate button definitions in XAML.
        /// The wall is generated programmatically.
        /// </summary>
        private void BuildWallGrid()
        {
            // Clear existing children first in case this method is ever called again.
            WallGrid.Children.Clear();

            // Loop over every row in the wall.
            for (int row = 0; row < WallFrame.Rows; row++)
            {
                // Loop over every column in the wall.
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    // Create one button to represent one wall cell.
                    var button = new Button
                    {
                        Margin = new Thickness(8),
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,

                        // Store row/column information directly on the button
                        // so that click handlers can identify which cell was pressed.
                        Tag = new CellPosition(row, column)
                    };

                    // All wall buttons share the same click handler.
                    button.Click += CellButton_Click;

                    // Add the button to the UI grid.
                    WallGrid.Children.Add(button);
                }
            }
        }

        /// <summary>
        /// Re-renders the visible simulator based on the current _wallFrame state.
        ///
        /// This is one of the most important methods in the current app.
        ///
        /// Mental model:
        /// - _wallFrame contains the true wall state
        /// - RenderWall() makes the UI match that truth
        ///
        /// If the frame changes, call RenderWall().
        /// </summary>
        private void RenderWall()
        {
            // Loop through every child element inside the UniformGrid.
            foreach (var child in WallGrid.Children)
            {
                // Defensive check: only continue if this child is actually a Button.
                if (child is not Button button)
                {
                    continue;
                }

                // Defensive check: only continue if the button has a valid CellPosition in Tag.
                if (button.Tag is not CellPosition position)
                {
                    continue;
                }

                // Ask the WallFrame whether this row/column is ON or OFF.
                bool isOn = _wallFrame.GetCell(position.Row, position.Column);

                // Update the button's visible label.
                button.Content = isOn ? "ON" : "OFF";

                // Update the button's background color.
                // ON  = warm yellow
                // OFF = dark gray
                button.Background = isOn
                    ? new SolidColorBrush(Color.FromRgb(255, 199, 0))
                    : new SolidColorBrush(Color.FromRgb(70, 70, 70));

                // Update text color for contrast/readability.
                button.Foreground = isOn
                    ? Brushes.Black
                    : Brushes.White;

                // Update border color so ON buttons feel more illuminated.
                button.BorderBrush = isOn
                    ? new SolidColorBrush(Color.FromRgb(255, 220, 120))
                    : new SolidColorBrush(Color.FromRgb(110, 110, 110));
            }
        }

        /// <summary>
        /// Updates the small speed text label beside the slider.
        ///
        /// Example:
        /// - slider at 100 -> "100%"
        /// - slider at 150 -> "150%"
        /// </summary>
        private void UpdateSpeedText()
        {
            SpeedValueTextBlock.Text = $"{(int)SpeedSlider.Value}%";
        }

        /// <summary>
        /// Converts a base interval into a speed-adjusted interval.
        ///
        /// Example:
        /// - base interval = 200 ms
        /// - speed = 100%  -> 200 ms actual
        /// - speed = 200%  -> 100 ms actual (faster)
        /// - speed = 50%   -> 400 ms actual (slower)
        ///
        /// We clamp the result to a minimum value so absurdly fast timer
        /// intervals do not become unstable or unreadable.
        /// </summary>
        private int GetAdjustedIntervalMs(int baseIntervalMs)
        {
            double speedMultiplier = SpeedSlider.Value / 100.0;
            int adjustedInterval = (int)Math.Round(baseIntervalMs / speedMultiplier);

            return Math.Max(30, adjustedInterval);
        }

        /// <summary>
        /// Applies the current slider-controlled speed to the timer interval.
        ///
        /// This is separated into its own method so it can be reused whenever:
        /// - a new animation starts
        /// - the slider changes while an animation is already running
        /// </summary>
        private void ApplyCurrentSpeedToTimer()
        {
            int adjustedInterval = GetAdjustedIntervalMs(_baseAnimationIntervalMs);
            _animationTimer.Interval = TimeSpan.FromMilliseconds(adjustedInterval);
        }

        /// <summary>
        /// Starts a prebuilt frame-list animation.
        ///
        /// This is used for animations that already exist as ordered frame
        /// sequences, such as row sweep or border pulse.
        ///
        /// Flow:
        /// 1. clear any procedural animation
        /// 2. store the frame list
        /// 3. reset animation index
        /// 4. store the base interval
        /// 5. apply speed control
        /// 6. show the first frame immediately
        /// 7. update status text
        /// 8. start the timer
        /// </summary>
        private void StartFrameAnimation(List<WallFrame> frames, int baseIntervalMs, string animationName)
        {
            if (frames.Count == 0)
            {
                return;
            }

            _proceduralFrameGenerator = null;
            _proceduralStep = 0;

            _animationFrames = frames;
            _animationFrameIndex = 0;

            _baseAnimationIntervalMs = baseIntervalMs;
            ApplyCurrentSpeedToTimer();

            _wallFrame.CopyFrom(_animationFrames[_animationFrameIndex]);
            RenderWall();

            AnimationStatusTextBlock.Text = $"Animation: {animationName}";
            _animationTimer.Start();
        }

        /// <summary>
        /// Starts a procedural animation.
        ///
        /// This is used when frames are generated on demand instead of
        /// coming from a prebuilt list.
        ///
        /// The generator function will be called each tick with the current step.
        /// </summary>
        private void StartProceduralAnimation(Func<int, WallFrame> frameGenerator, int baseIntervalMs, string animationName)
        {
            _animationFrames.Clear();
            _animationFrameIndex = 0;

            _proceduralFrameGenerator = frameGenerator;
            _proceduralStep = 0;

            _baseAnimationIntervalMs = baseIntervalMs;
            ApplyCurrentSpeedToTimer();

            // Generate and show the very first frame immediately.
            _wallFrame.CopyFrom(_proceduralFrameGenerator(_proceduralStep));
            RenderWall();

            AnimationStatusTextBlock.Text = $"Animation: {animationName}";
            _animationTimer.Start();
        }

        /// <summary>
        /// Stops any currently running animation and resets playback state.
        ///
        /// This is called when:
        /// - the user clicks Stop Animation
        /// - the user manually edits the wall
        /// - the user applies a static pattern
        /// </summary>
        private void StopAnimation()
        {
            _animationTimer.Stop();

            _animationFrames.Clear();
            _animationFrameIndex = 0;

            _proceduralFrameGenerator = null;
            _proceduralStep = 0;

            AnimationStatusTextBlock.Text = "Animation: stopped";
        }

        /// <summary>
        /// Timer tick handler.
        ///
        /// This runs repeatedly while the timer is active.
        ///
        /// There are two playback modes:
        /// 1. procedural animation mode
        /// 2. prebuilt frame-list mode
        ///
        /// The method checks which mode is active and advances accordingly.
        /// </summary>
        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            // Procedural animation mode:
            // generate a fresh frame from the current step number.
            if (_proceduralFrameGenerator is not null)
            {
                _proceduralStep++;
                _wallFrame.CopyFrom(_proceduralFrameGenerator(_proceduralStep));
                RenderWall();
                return;
            }

            // Prebuilt frame-list mode:
            // step through the stored list of frames.
            if (_animationFrames.Count == 0)
            {
                return;
            }

            _animationFrameIndex++;

            if (_animationFrameIndex >= _animationFrames.Count)
            {
                _animationFrameIndex = 0;
            }

            _wallFrame.CopyFrom(_animationFrames[_animationFrameIndex]);
            RenderWall();
        }

        /// <summary>
        /// Handles clicks on individual wall cells in the simulator.
        ///
        /// This allows the user to manually toggle bulbs ON/OFF.
        /// </summary>
        private void CellButton_Click(object sender, RoutedEventArgs e)
        {
            // Manual editing should stop any running animation first.
            StopAnimation();

            // Make sure sender is actually a Button.
            if (sender is not Button button)
            {
                return;
            }

            // Make sure the button has row/column metadata in Tag.
            if (button.Tag is not CellPosition position)
            {
                return;
            }

            // Toggle the corresponding cell in the wall model.
            _wallFrame.ToggleCell(position.Row, position.Column);

            // Redraw the UI to match the updated wall state.
            RenderWall();
        }

        /// <summary>
        /// Clears the wall (all OFF).
        /// </summary>
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            _wallFrame.Clear();
            RenderWall();
        }

        /// <summary>
        /// Fills the wall (all ON).
        /// </summary>
        private void FillButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            _wallFrame.Fill();
            RenderWall();
        }

        /// <summary>
        /// Applies a random ON/OFF state across the wall.
        /// Useful for quick testing and visual variation.
        /// </summary>
        private void RandomizeButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            _wallFrame.Randomize(_random);
            RenderWall();
        }

        /// <summary>
        /// Lights up the human-labeled "Row 3".
        ///
        /// Important indexing note:
        /// humans count 1, 2, 3...
        /// code counts   0, 1, 2...
        ///
        /// So Row 3 in the UI corresponds to row index 2 in code.
        /// </summary>
        private void RowThreeButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            _wallFrame.Clear();
            _wallFrame.SetRow(2, true);
            RenderWall();
        }

        /// <summary>
        /// Lights up the human-labeled "Column 4".
        ///
        /// Human column 4 corresponds to code index 3.
        /// </summary>
        private void ColumnFourButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            _wallFrame.Clear();
            _wallFrame.SetColumn(3, true);
            RenderWall();
        }

        /// <summary>
        /// Applies the checkerboard pattern from WallPatterns.
        /// </summary>
        private void CheckerboardButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            WallPatterns.ApplyCheckerboard(_wallFrame);
            RenderWall();
        }

        /// <summary>
        /// Applies the border pattern from WallPatterns.
        /// </summary>
        private void BorderButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            WallPatterns.ApplyBorder(_wallFrame);
            RenderWall();
        }

        /// <summary>
        /// Applies the cross pattern from WallPatterns.
        /// </summary>
        private void CrossButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            WallPatterns.ApplyCross(_wallFrame);
            RenderWall();
        }

        /// <summary>
        /// Applies a simple random sparkle pattern from WallPatterns.
        ///
        /// The number 8 here means:
        /// "attempt to light roughly 8 random cells."
        /// </summary>
        private void SparkleButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            WallPatterns.ApplyRandomSparkle(_wallFrame, _random, 8);
            RenderWall();
        }

        /// <summary>
        /// Starts the prebuilt row sweep animation.
        /// </summary>
        private void StartRowSweepButton_Click(object sender, RoutedEventArgs e)
        {
            StartFrameAnimation(
                WallAnimations.CreateRowSweepFrames(),
                180,
                "row sweep");
        }

        /// <summary>
        /// Starts the prebuilt border pulse animation.
        /// </summary>
        private void StartBorderPulseButton_Click(object sender, RoutedEventArgs e)
        {
            StartFrameAnimation(
                WallAnimations.CreateBorderPulseFrames(),
                240,
                "border pulse");
        }

        /// <summary>
        /// Starts the procedural meteor animation.
        ///
        /// This is a good example of an animation generated from rules rather
        /// than from a fixed prebuilt list.
        /// </summary>
        private void StartMeteorButton_Click(object sender, RoutedEventArgs e)
        {
            StartProceduralAnimation(
                WallProceduralAnimations.GenerateMeteorFrame,
                120,
                "procedural meteor");
        }

        /// <summary>
        /// Starts the procedural sparkle storm animation.
        ///
        /// This uses a lambda because the generator needs access to the shared
        /// Random instance owned by this window.
        /// </summary>
        private void StartSparkleStormButton_Click(object sender, RoutedEventArgs e)
        {
            StartProceduralAnimation(
                step => WallProceduralAnimations.GenerateSparkleStormFrame(step, _random),
                110,
                "procedural sparkle storm");
        }

        /// <summary>
        /// Stops the currently running animation, if any.
        /// </summary>
        private void StopAnimationButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
        }

        /// <summary>
        /// Runs whenever the speed slider value changes.
        ///
        /// This does two things:
        /// 1. updates the visible percentage text
        /// 2. immediately applies the new speed to the timer if animation is running
        ///
        /// That means you can adjust animation speed live while watching it.
        /// </summary>
        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // During startup, ValueChanged may fire as the XAML initializes.
            // This guard keeps us safe if the named UI elements are not fully ready yet.
            if (SpeedValueTextBlock is null)
            {
                return;
            }

            UpdateSpeedText();

            if (_animationTimer.IsEnabled)
            {
                ApplyCurrentSpeedToTimer();
            }
        }

        /// <summary>
        /// Small helper type used to attach row/column information
        /// to each simulator button.
        ///
        /// Every wall cell button stores one of these in its Tag property.
        ///
        /// Example:
        /// - Row = 2
        /// - Column = 4
        ///
        /// That lets event handlers know exactly which wall cell was clicked.
        /// </summary>
        private record CellPosition(int Row, int Column);
    }
}