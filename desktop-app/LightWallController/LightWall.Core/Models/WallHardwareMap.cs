using System;

namespace LightWall.Core.Models
{
    /// <summary>
    /// Translates between the three different ways this wall's bulbs get named.
    ///
    /// THREE NAMES FOR THE SAME BULB
    ///
    /// The top-left bulb is called all of these, depending on who is talking:
    ///
    ///   bulb 0        the app's numbering, and the bit position in a packet
    ///   row 0, col 0  the wall model's coordinates
    ///   "A1"          the sticker on the relay that switches it
    ///   pin 2         the Arduino output driving that relay
    ///
    /// Having all four in one place matters most during hardware bring-up. When
    /// the wall does something unexpected, the question is always "which of
    /// these four is disagreeing with the others?", and answering it means being
    /// able to move between them freely.
    ///
    /// WHERE THE RELAY LABELS COME FROM
    ///
    /// Every relay in the enclosure carries a sticker: A1 to A7, B1 to B7, and
    /// so on through E7. Thirty-five of them, which is the whole wall.
    ///
    /// The letter is the row and the number is the column, counting from 1.
    /// That is not a guess - the original sketch uses the same convention:
    ///
    ///   rowAEOff() touches lights[0] and lights[4]   so A = 0 and E = 4
    ///   rowBDOff() touches lights[1] and lights[3]   so B = 1 and D = 3
    ///   rowCOff()  touches lights[2]                 so C = 2
    ///   col4On()   touches lights[r][3]              so column 4 = index 3
    ///
    /// The person who built the wall and the person who wrote the sketch agreed
    /// with each other, which is a much stronger footing than either alone.
    ///
    /// STILL WORTH VERIFYING
    ///
    /// None of the above proves that relay A1 physically switches the top-left
    /// bulb. It proves the labelling scheme is consistent, not that the wiring
    /// matches it. Only lighting one bulb and looking at the wall settles that,
    /// which is what the bulb identification mode is for.
    /// </summary>
    public static class WallHardwareMap
    {
        /// <summary>
        /// Which Arduino pin drives each bulb, in row-major order.
        ///
        /// Copied from allLights[35] in the original working sketch, which is
        /// the only authoritative source for this. Note the jump from 13 to 22
        /// partway through row B - pins 14 to 21 are skipped, most likely
        /// because they carry serial and other functions on a Mega.
        ///
        /// Bulb number N is at ArduinoPins[N], with no translation needed,
        /// because our bit numbering and the sketch's array happen to use the
        /// same row-major order.
        /// </summary>
        private static readonly int[] ArduinoPins =
        {
            //  col1 col2 col3 col4 col5 col6 col7
                 2,   3,   4,   5,   6,   7,   8,   // row A
                 9,  10,  11,  12,  13,  22,  23,   // row B
                24,  25,  26,  27,  28,  29,  30,   // row C
                31,  32,  33,  34,  35,  36,  37,   // row D
                38,  39,  40,  41,  42,  43,  44    // row E
        };

        /// <summary>
        /// The letter naming each row, top to bottom.
        /// </summary>
        private const string RowLetters = "ABCDE";

        /// <summary>
        /// How many bulbs there are in total.
        /// </summary>
        public static int BulbCount => WallFrame.Rows * WallFrame.Columns;

        /// <summary>
        /// Turns a bulb number into the label printed on its relay.
        ///
        /// Bulb 0 gives "A1"; bulb 34 gives "E7".
        /// </summary>
        public static string GetRelayLabel(int bulbIndex)
        {
            ValidateBulbIndex(bulbIndex);

            int row = bulbIndex / WallFrame.Columns;
            int column = bulbIndex % WallFrame.Columns;

            // The column is written the way a person counts, starting at 1,
            // because that is what the sticker says.
            return $"{RowLetters[row]}{column + 1}";
        }

        /// <summary>
        /// Turns a bulb number into the Arduino pin that drives it.
        /// </summary>
        public static int GetArduinoPin(int bulbIndex)
        {
            ValidateBulbIndex(bulbIndex);
            return ArduinoPins[bulbIndex];
        }

        /// <summary>
        /// Turns a row and column into a bulb number.
        /// </summary>
        public static int GetBulbIndex(int row, int column)
        {
            if (row < 0 || row >= WallFrame.Rows)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            if (column < 0 || column >= WallFrame.Columns)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }

            return (row * WallFrame.Columns) + column;
        }

        /// <summary>
        /// Turns a bulb number back into row and column coordinates.
        /// </summary>
        public static (int Row, int Column) GetPosition(int bulbIndex)
        {
            ValidateBulbIndex(bulbIndex);

            return (bulbIndex / WallFrame.Columns, bulbIndex % WallFrame.Columns);
        }

        /// <summary>
        /// Reads a relay label such as "C4" and works out which bulb it is.
        ///
        /// Accepts lower case and surrounding spaces, because this is fed by a
        /// person typing while standing at the wall reading a sticker, quite
        /// possibly in bright sun with a phone in the other hand.
        ///
        /// Returns false rather than throwing on bad input, since a typo is an
        /// ordinary thing to expect here rather than a programming error.
        /// </summary>
        public static bool TryParseRelayLabel(string? label, out int bulbIndex)
        {
            bulbIndex = -1;

            if (string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            string trimmed = label.Trim();

            if (trimmed.Length != 2)
            {
                return false;
            }

            int row = RowLetters.IndexOf(char.ToUpperInvariant(trimmed[0]));

            if (row < 0)
            {
                return false;
            }

            // The sticker counts columns from 1, so "C4" is column index 3.
            if (!int.TryParse(trimmed.AsSpan(1, 1), out int columnNumber))
            {
                return false;
            }

            if (columnNumber < 1 || columnNumber > WallFrame.Columns)
            {
                return false;
            }

            bulbIndex = (row * WallFrame.Columns) + (columnNumber - 1);
            return true;
        }

        /// <summary>
        /// Builds a one-line description of a bulb naming it every way at once.
        ///
        /// For example: "Bulb 23 of 34   row 4, col 3   relay D3   Arduino pin 33"
        ///
        /// "of 34" rather than "of 35" on purpose, and it is the one number here
        /// not written the way a person counts. Bulb numbers run 0 to 34 because
        /// they are also bit positions in a packet, so naming the last one is
        /// more use at the wall than naming the total - it says where the
        /// stepping stops. Rows and columns beside it ARE written from 1,
        /// because those match nothing but the sticker.
        ///
        /// This is what the identification mode shows while walking the wall.
        /// All four names together mean the reading can be checked against the
        /// sticker, the pin and the physical bulb without doing arithmetic in
        /// your head halfway up a ladder.
        ///
        /// Rows and columns are written the way a person counts, from 1, because
        /// this line is read by a human rather than by code.
        /// </summary>
        public static string Describe(int bulbIndex)
        {
            ValidateBulbIndex(bulbIndex);

            (int row, int column) = GetPosition(bulbIndex);

            return $"Bulb {bulbIndex} of {BulbCount - 1}    " +
                   $"row {row + 1}, col {column + 1}    " +
                   $"relay {GetRelayLabel(bulbIndex)}    " +
                   $"Arduino pin {GetArduinoPin(bulbIndex)}";
        }

        /// <summary>
        /// Makes sure a bulb number is one of the 35 that exist.
        /// </summary>
        private static void ValidateBulbIndex(int bulbIndex)
        {
            if (bulbIndex < 0 || bulbIndex >= WallFrame.Rows * WallFrame.Columns)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bulbIndex),
                    $"Bulb number must be between 0 and {WallFrame.Rows * WallFrame.Columns - 1}.");
            }
        }
    }
}
