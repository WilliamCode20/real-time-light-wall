using System;
using System.Collections.Generic;
using System.Text;
using LightWall.Core.Models;

namespace LightWall.Core.Patterns
{
    /// <summary>
    /// Contains reusable static pattern-building methods.
    ///
    /// These methods do not create animation timing.
    /// They simply fill a WallFrame with a particular static arrangement.
    ///
    /// This class exists to keep pattern logic out of the user interface.
    /// That is an important design decision.
    ///
    /// WHERE THIS FITS
    ///
    /// These are drawing routines, not effects. They know how to paint an
    /// arrangement but nothing about time, playback or selection.
    ///
    /// EffectCatalog wraps each of them in a StaticPatternEffect, which is what
    /// gives them a name, a description, and a place in the interface. The
    /// division is deliberate: the same routine can be reused by several
    /// effects. WallAnimations calls ApplyBorder and ApplyCross to build the
    /// Border Pulse sequence, for instance.
    /// </summary>
    public static class WallPatterns
    {
        /// <summary>
        /// Applies a checkerboard pattern to the given frame.
        ///
        /// The rule is:
        /// - if row + column is even, turn the cell ON
        /// - otherwise leave it OFF
        ///
        /// This creates an alternating grid pattern.
        /// </summary>
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

        /// <summary>
        /// Applies a border pattern.
        ///
        /// A border cell is any cell that sits on the outside edge:
        /// - top row
        /// - bottom row
        /// - left column
        /// - right column
        ///
        /// Everything inside the border stays OFF.
        /// </summary>
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

        /// <summary>
        /// Applies a cross / plus-sign pattern.
        ///
        /// This turns on:
        /// - the center row
        /// - the center column
        ///
        /// On a 5x7 grid, that means:
        /// - center row    = 2
        /// - center column = 3
        /// </summary>
        public static void ApplyCross(WallFrame frame)
        {
            frame.Clear();

            int centerRow = WallFrame.Rows / 2;
            int centerColumn = WallFrame.Columns / 2;

            frame.SetRow(centerRow, true);
            frame.SetColumn(centerColumn, true);
        }

        /// <summary>
        /// Applies a simple random sparkle pattern by turning on a given
        /// number of random cells.
        ///
        /// Note:
        /// Some random picks may land on the same cell more than once,
        /// so the final number of lit cells may be slightly less than 'count'.
        ///
        /// That is okay for this early prototype.
        /// </summary>
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