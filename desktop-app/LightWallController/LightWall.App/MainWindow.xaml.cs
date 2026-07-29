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
        /// The single "active" wall state currently being displayed.
        ///
        /// This object is the truth of what the simulated wall looks like.
        /// The UI does not store wall truth directly; it reads from this object.
        /// </summary>
        private readonly WallFrame _wallFrame = new();

        /// <summary>
        /// Shared random number generator for pattern/random operations.
        ///
        /// Keeping one Random instance is better than recreating one repeatedly.
        /// </summary>
        private readonly Random _random = new();

        /// <summary>
        /// WPF timer used to drive animation playback.
        ///
        /// Unlike blocking wait/delay logic, this lets the UI keep responding
        /// while animation updates happen over time.
        /// </summary>
        private readonly DispatcherTimer _animationTimer = new();

        /// <summary>
        /// The currently loaded animation frame sequence.
        ///
        /// Example:
        /// - row sweep frames
        /// - border pulse frames
        ///
        /// The timer advances through this list over time.
        /// </summary>
        private List<WallFrame> _animationFrames = new();

        /// <summary>
        /// Index of the currently displayed frame in the animation sequence.
        /// </summary>
        private int _animationFrameIndex = 0;

        /// <summary>
        /// Constructor for the main window.
        ///
        /// This runs when the app creates the window.
        ///
        /// Startup sequence:
        /// 1. InitializeComponent() loads and connects the XAML
        /// 2. ConfigureAnimationTimer() prepares timer-based animation playback
        /// 3. BuildWallGrid() creates the 35 simulator buttons
        /// 4. RenderWall() paints the initial state to the UI
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            ConfigureAnimationTimer();
            BuildWallGrid();
            RenderWall();
        }

        /// <summary>
        /// Configures the animation timer.
        ///
        /// Important concepts:
        /// - Interval = how often the timer ticks
        /// - Tick event = what method runs each time the timer fires
        ///
        /// The interval here is only a default; StartAnimation(...) can change it.
        /// </summary>
        private void ConfigureAnimationTimer()
        {
            _animationTimer.Interval = TimeSpan.FromMilliseconds(180);
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
        /// Starts playback of a prepared animation sequence.
        ///
        /// Parameters:
        /// - frames: the ordered list of WallFrame objects to play
        /// - intervalMs: how many milliseconds between frames
        /// - animationName: text label shown in the UI status
        ///
        /// Flow:
        /// 1. store the frames
        /// 2. reset frame index
        /// 3. set timer speed
        /// 4. show first frame immediately
        /// 5. update UI status
        /// 6. start timer
        /// </summary>
        private void StartAnimation(List<WallFrame> frames, int intervalMs, string animationName)
        {
            // If there are no frames, there's nothing to animate.
            if (frames.Count == 0)
            {
                return;
            }

            _animationFrames = frames;
            _animationFrameIndex = 0;

            // Set timer speed for this specific animation.
            _animationTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);

            // Immediately show the first frame.
            _wallFrame.CopyFrom(_animationFrames[_animationFrameIndex]);
            RenderWall();

            // Update the visible status text.
            AnimationStatusTextBlock.Text = $"Animation: {animationName}";

            // Begin timer-driven playback.
            _animationTimer.Start();
        }

        /// <summary>
        /// Stops animation playback and resets animation state.
        ///
        /// This is called when:
        /// - the user clicks Stop Animation
        /// - the user manually edits cells
        /// - the user applies a static pattern
        ///
        /// Why stop animation before manual operations?
        /// Because otherwise the timer would immediately overwrite the user's change.
        /// </summary>
        private void StopAnimation()
        {
            _animationTimer.Stop();
            _animationFrames.Clear();
            _animationFrameIndex = 0;
            AnimationStatusTextBlock.Text = "Animation: stopped";
        }

        /// <summary>
        /// Runs every time the animation timer ticks.
        ///
        /// This advances the animation by one frame and re-renders the wall.
        ///
        /// Flow:
        /// 1. ensure frames exist
        /// 2. move to next frame index
        /// 3. wrap around if needed
        /// 4. copy next frame into _wallFrame
        /// 5. render updated wall
        /// </summary>
        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            if (_animationFrames.Count == 0)
            {
                return;
            }

            // Advance to the next frame.
            _animationFrameIndex++;

            // Loop back to the beginning if we hit the end.
            if (_animationFrameIndex >= _animationFrames.Count)
            {
                _animationFrameIndex = 0;
            }

            // Copy the new frame into the active wall state and redraw.
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
        /// Starts the row sweep animation.
        ///
        /// This uses:
        /// - WallAnimations to create the frame sequence
        /// - StartAnimation(...) to play those frames over time
        /// </summary>
        private void StartRowSweepButton_Click(object sender, RoutedEventArgs e)
        {
            StartAnimation(
                WallAnimations.CreateRowSweepFrames(),
                180,
                "row sweep");
        }

        /// <summary>
        /// Starts the border pulse animation.
        /// </summary>
        private void StartBorderPulseButton_Click(object sender, RoutedEventArgs e)
        {
            StartAnimation(
                WallAnimations.CreateBorderPulseFrames(),
                240,
                "border pulse");
        }

        /// <summary>
        /// Stops the currently running animation, if any.
        /// </summary>
        private void StopAnimationButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
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