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
    public partial class MainWindow : Window
    {
        private readonly WallFrame _wallFrame = new();
        private readonly Random _random = new();
        private readonly DispatcherTimer _animationTimer = new();

        private List<WallFrame> _animationFrames = new();
        private int _animationFrameIndex = 0;

        public MainWindow()
        {
            InitializeComponent();
            ConfigureAnimationTimer();
            BuildWallGrid();
            RenderWall();
        }

        private void ConfigureAnimationTimer()
        {
            _animationTimer.Interval = TimeSpan.FromMilliseconds(180);
            _animationTimer.Tick += AnimationTimer_Tick;
        }

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
                        Tag = new CellPosition(row, column)
                    };

                    button.Click += CellButton_Click;
                    WallGrid.Children.Add(button);
                }
            }
        }

        private void RenderWall()
        {
            foreach (var child in WallGrid.Children)
            {
                if (child is not Button button)
                {
                    continue;
                }

                if (button.Tag is not CellPosition position)
                {
                    continue;
                }

                bool isOn = _wallFrame.GetCell(position.Row, position.Column);

                button.Content = isOn ? "ON" : "OFF";
                button.Background = isOn
                    ? new SolidColorBrush(Color.FromRgb(255, 199, 0))
                    : new SolidColorBrush(Color.FromRgb(70, 70, 70));
                button.Foreground = isOn
                    ? Brushes.Black
                    : Brushes.White;
                button.BorderBrush = isOn
                    ? new SolidColorBrush(Color.FromRgb(255, 220, 120))
                    : new SolidColorBrush(Color.FromRgb(110, 110, 110));
            }
        }

        private void StartAnimation(List<WallFrame> frames, int intervalMs, string animationName)
        {
            if (frames.Count == 0)
            {
                return;
            }

            _animationFrames = frames;
            _animationFrameIndex = 0;

            _animationTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);

            _wallFrame.CopyFrom(_animationFrames[_animationFrameIndex]);
            RenderWall();

            AnimationStatusTextBlock.Text = $"Animation: {animationName}";
            _animationTimer.Start();
        }

        private void StopAnimation()
        {
            _animationTimer.Stop();
            _animationFrames.Clear();
            _animationFrameIndex = 0;
            AnimationStatusTextBlock.Text = "Animation: stopped";
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
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

        private void CellButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();

            if (sender is not Button button)
            {
                return;
            }

            if (button.Tag is not CellPosition position)
            {
                return;
            }

            _wallFrame.ToggleCell(position.Row, position.Column);
            RenderWall();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            _wallFrame.Clear();
            RenderWall();
        }

        private void FillButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            _wallFrame.Fill();
            RenderWall();
        }

        private void RandomizeButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            _wallFrame.Randomize(_random);
            RenderWall();
        }

        private void RowThreeButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            _wallFrame.Clear();
            _wallFrame.SetRow(2, true);
            RenderWall();
        }

        private void ColumnFourButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            _wallFrame.Clear();
            _wallFrame.SetColumn(3, true);
            RenderWall();
        }

        private void CheckerboardButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            WallPatterns.ApplyCheckerboard(_wallFrame);
            RenderWall();
        }

        private void BorderButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            WallPatterns.ApplyBorder(_wallFrame);
            RenderWall();
        }

        private void CrossButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            WallPatterns.ApplyCross(_wallFrame);
            RenderWall();
        }

        private void SparkleButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            WallPatterns.ApplyRandomSparkle(_wallFrame, _random, 8);
            RenderWall();
        }

        private void StartRowSweepButton_Click(object sender, RoutedEventArgs e)
        {
            StartAnimation(
                WallAnimations.CreateRowSweepFrames(),
                180,
                "row sweep");
        }

        private void StartBorderPulseButton_Click(object sender, RoutedEventArgs e)
        {
            StartAnimation(
                WallAnimations.CreateBorderPulseFrames(),
                240,
                "border pulse");
        }

        private void StopAnimationButton_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
        }

        private record CellPosition(int Row, int Column);
    }
}