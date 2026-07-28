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

        public void SetAll(bool value)
        {
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    _cells[row, column] = value;
                }
            }
        }

        public void SetRow(int row, bool value)
        {
            if (row < 0 || row >= Rows)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            for (int column = 0; column < Columns; column++)
            {
                _cells[row, column] = value;
            }
        }

        public void SetColumn(int column, bool value)
        {
            if (column < 0 || column >= Columns)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }

            for (int row = 0; row < Rows; row++)
            {
                _cells[row, column] = value;
            }
        }

        public void Clear()
        {
            SetAll(false);
        }

        public void Fill()
        {
            SetAll(true);
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