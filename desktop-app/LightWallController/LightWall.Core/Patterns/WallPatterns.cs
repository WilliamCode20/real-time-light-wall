using System;
using System.Collections.Generic;
using System.Text;
using LightWall.Core.Models;

namespace LightWall.Core.Patterns
{
    public static class WallPatterns
    {
        public static void ApplyCheckerboard(WallFrame frame)
        {
            frame.Clear();

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    bool isOn = (row + column) % 2 == 0;
                    frame.SetCell(row, column, isOn);
                }
            }
        }

        public static void ApplyBorder(WallFrame frame)
        {
            frame.Clear();

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    bool isBorderCell =
                        row == 0 ||
                        row == WallFrame.Rows - 1 ||
                        column == 0 ||
                        column == WallFrame.Columns - 1;

                    frame.SetCell(row, column, isBorderCell);
                }
            }
        }

        public static void ApplyCross(WallFrame frame)
        {
            frame.Clear();

            int centerRow = WallFrame.Rows / 2;
            int centerColumn = WallFrame.Columns / 2;

            frame.SetRow(centerRow, true);
            frame.SetColumn(centerColumn, true);
        }

        public static void ApplyRandomSparkle(WallFrame frame, Random random, int count)
        {
            frame.Clear();

            for (int i = 0; i < count; i++)
            {
                int row = random.Next(0, WallFrame.Rows);
                int column = random.Next(0, WallFrame.Columns);
                frame.SetCell(row, column, true);
            }
        }
    }
}