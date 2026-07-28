using System;
using System.Collections.Generic;
using System.Text;

namespace LightWall.Core.Models
{
    public class WallFrame
    {
        public const int Rows = 5;
        public const int Columns = 7;

        private readonly bool[,] _cells = new bool[Rows, Columns];

        public bool GetCell(int row, int column)
        {
            ValidateCoordinates(row, column);
            return _cells[row, column];
        }

        public void SetCell(int row, int column, bool value)
        {
            ValidateCoordinates(row, column);
            _cells[row, column] = value;
        }

        public void ToggleCell(int row, int column)
        {
            ValidateCoordinates(row, column);
            _cells[row, column] = !_cells[row, column];
        }

        public void Clear()
        {
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    _cells[row, column] = false;
                }
            }
        }

        public void Fill()
        {
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    _cells[row, column] = true;
                }
            }
        }

        public void Randomize(Random random)
        {
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    _cells[row, column] = random.Next(0, 2) == 1;
                }
            }
        }

        private static void ValidateCoordinates(int row, int column)
        {
            if (row < 0 || row >= Rows)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            if (column < 0 || column >= Columns)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }
        }
    }
}