using System;
using System.Collections.Generic;
using System.Text;

namespace LightWall.Core.Models
{
    /// <summary>
    /// Represents a single 5x7 "snapshot" of the light wall.
    ///
    /// This class does NOT know anything about:
    /// - WPF / the user interface
    /// - animation timers
    /// - audio
    /// - Arduino serial communication
    ///
    /// Its only job is to store the ON/OFF state of each cell in the wall
    /// and provide small helper methods for manipulating that state.
    ///
    /// You can think of WallFrame as:
    /// "What should the wall look like right now?"
    ///
    /// Later, other parts of the app will:
    /// - render this frame in the simulator UI
    /// - send this frame to the Arduino
    /// - generate new frames from animation or audio logic
    /// </summary>
    public class WallFrame
    {
        /// <summary>
        /// The wall has 5 rows.
        /// This is a constant because the physical wall dimensions are fixed.
        /// </summary>
        public const int Rows = 5;

        /// <summary>
        /// The wall has 7 columns.
        /// This is a constant because the physical wall dimensions are fixed.
        /// </summary>
        public const int Columns = 7;

        /// <summary>
        /// Internal storage for the wall cells.
        ///
        /// This is a 2D array of booleans:
        /// - true  = light ON
        /// - false = light OFF
        ///
        /// The first index is row, the second is column.
        /// Example:
        /// _cells[2, 3] means row 2, column 3.
        /// </summary>
        private readonly bool[,] _cells = new bool[Rows, Columns];

        /// <summary>
        /// Returns the ON/OFF value for one specific cell.
        ///
        /// This is a "read" operation.
        /// It does not change the frame; it only looks up the current state.
        /// </summary>
        public bool GetCell(int row, int column)
        {
            ValidateCoordinates(row, column);
            return _cells[row, column];
        }

        /// <summary>
        /// Sets one specific cell to either ON or OFF.
        ///
        /// This is the most basic write operation in the class.
        /// Many higher-level operations ultimately depend on this idea.
        /// </summary>
        public void SetCell(int row, int column, bool value)
        {
            ValidateCoordinates(row, column);
            _cells[row, column] = value;
        }

        /// <summary>
        /// Flips one specific cell:
        /// - if it was OFF, it becomes ON
        /// - if it was ON, it becomes OFF
        ///
        /// This is useful for interactive clicking in the simulator.
        /// </summary>
        public void ToggleCell(int row, int column)
        {
            ValidateCoordinates(row, column);
            _cells[row, column] = !_cells[row, column];
        }

        /// <summary>
        /// Sets every cell in the frame to the same value.
        ///
        /// This is a primitive wall operation.
        /// It is more general than Clear() or Fill().
        ///
        /// Examples:
        /// SetAll(false) -> all OFF
        /// SetAll(true)  -> all ON
        /// </summary>
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

        /// <summary>
        /// Sets an entire row to ON or OFF.
        ///
        /// Example:
        /// SetRow(2, true) turns on the middle row.
        ///
        /// This is useful for:
        /// - row-based test operations
        /// - sweeps
        /// - equalizer-like visuals
        /// </summary>
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

        /// <summary>
        /// Sets an entire column to ON or OFF.
        ///
        /// Example:
        /// SetColumn(3, true) turns on the center column.
        ///
        /// This is useful for:
        /// - column-based tests
        /// - visualizer bars
        /// - symmetry-based patterns
        /// </summary>
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

        /// <summary>
        /// Copies the contents of another WallFrame into this one.
        ///
        /// This is especially important for animation playback.
        /// The timer logic can generate a sequence of separate WallFrame objects,
        /// and then copy each one into the "active" wall frame displayed by the UI.
        ///
        /// In plain English:
        /// "Make this frame look exactly like the other frame."
        /// </summary>
        public void CopyFrom(WallFrame other)
        {
            if (other is null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    _cells[row, column] = other.GetCell(row, column);
                }
            }
        }

        /// <summary>
        /// Creates a new WallFrame whose lit cells are shifted by the supplied
        /// row and column offsets.
        ///
        /// Positive row offset moves content downward.
        /// Positive column offset moves content to the right.
        ///
        /// Cells shifted outside the 5x7 wall are discarded.
        ///
        /// This is useful for "centering" or offset controls because it lets
        /// us move the actual frame data itself rather than faking the shift
        /// only in the UI render layer.
        /// </summary>
        public WallFrame CreateTranslated(int rowOffset, int columnOffset)
        {
            var translated = new WallFrame();

            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    if (!_cells[row, column])
                    {
                        continue;
                    }

                    int translatedRow = row + rowOffset;
                    int translatedColumn = column + columnOffset;

                    if (translatedRow >= 0 && translatedRow < Rows &&
                        translatedColumn >= 0 && translatedColumn < Columns)
                    {
                        translated.SetCell(translatedRow, translatedColumn, true);
                    }
                }
            }

            return translated;
        }

        /// <summary>
        /// Turns every cell OFF.
        ///
        /// This is just a convenience wrapper around SetAll(false).
        /// It exists because "Clear" is a nice readable action name.
        /// </summary>
        public void Clear()
        {
            SetAll(false);
        }

        /// <summary>
        /// Turns every cell ON.
        ///
        /// This is just a convenience wrapper around SetAll(true).
        /// </summary>
        public void Fill()
        {
            SetAll(true);
        }

        /// <summary>
        /// Randomly assigns ON/OFF values across the whole wall.
        ///
        /// This is useful for:
        /// - quick testing
        /// - rough sparkle/noise effects
        /// - visually checking that the simulator is working
        ///
        /// A Random object is passed in rather than created inside the method
        /// so that the caller controls randomness consistently.
        /// </summary>
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

        /// <summary>
        /// Makes sure the requested row/column exists inside the 5x7 wall.
        ///
        /// This prevents invalid array access and catches bugs early.
        ///
        /// Example invalid cases:
        /// - row = -1
        /// - row = 5
        /// - column = 7
        /// </summary>
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