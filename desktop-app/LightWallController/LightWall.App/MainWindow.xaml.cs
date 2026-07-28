using LightWall.Core.Models;
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

namespace LightWall.App
{
    public partial class MainWindow : Window
    {
        private readonly WallFrame _wallFrame = new();
        private readonly Random _random = new();

        public MainWindow()
        {
            InitializeComponent();
            BuildWallGrid();
            RenderWall();
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

        private void CellButton_Click(object sender, RoutedEventArgs e)
        {
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
            _wallFrame.Clear();
            RenderWall();
        }

        private void FillButton_Click(object sender, RoutedEventArgs e)
        {
            _wallFrame.Fill();
            RenderWall();
        }

        private void RandomizeButton_Click(object sender, RoutedEventArgs e)
        {
            _wallFrame.Randomize(_random);
            RenderWall();
        }

        private record CellPosition(int Row, int Column);
    }
}