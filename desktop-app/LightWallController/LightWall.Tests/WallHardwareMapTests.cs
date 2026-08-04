using System;
using System.Collections.Generic;
using System.Linq;
using LightWall.Core.Effects;
using LightWall.Core.Models;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the translation between bulb numbers, wall coordinates, relay
    /// labels and Arduino pins.
    ///
    /// These matter because the whole hardware bring-up session depends on them.
    /// If the app says "lighting relay C4" while actually lighting a different
    /// bulb, the mapping exercise would produce a confidently wrong table, and
    /// every effect afterwards would be subtly scrambled in a way that looks
    /// like a wiring fault.
    ///
    /// The pin numbers are checked against the original sketch by hand rather
    /// than computed, since that sketch is the only authoritative source for
    /// them.
    /// </summary>
    public class WallHardwareMapTests
    {
        [Fact]
        public void TheWallHasThirtyFiveBulbs()
        {
            Assert.Equal(35, WallHardwareMap.BulbCount);
        }

        /// <summary>
        /// The corners and the centre, written out by hand.
        ///
        /// The relay labels come from the stickers in the enclosure, and the
        /// pins from allLights[35] in the original sketch. Two independent
        /// sources agreeing is what gives confidence in the whole table.
        /// </summary>
        [Theory]
        [InlineData(0, "A1", 2)]    // top-left
        [InlineData(6, "A7", 8)]    // top-right
        [InlineData(7, "B1", 9)]    // start of row B
        [InlineData(12, "B6", 22)]  // the jump from pin 13 to pin 22
        [InlineData(13, "B7", 23)]
        [InlineData(17, "C4", 27)]  // dead centre of the wall
        [InlineData(23, "D3", 33)]
        [InlineData(28, "E1", 38)]  // bottom-left
        [InlineData(34, "E7", 44)]  // bottom-right
        public void KnownBulbs_HaveTheExpectedLabelAndPin(
            int bulbIndex,
            string expectedLabel,
            int expectedPin)
        {
            Assert.Equal(expectedLabel, WallHardwareMap.GetRelayLabel(bulbIndex));
            Assert.Equal(expectedPin, WallHardwareMap.GetArduinoPin(bulbIndex));
        }

        [Fact]
        public void RowLettersRunAToEDownTheWall()
        {
            // A is the top row and E is the bottom, matching the original
            // sketch's rowAEOff() touching lights[0] and lights[4].
            Assert.StartsWith("A", WallHardwareMap.GetRelayLabel(0));
            Assert.StartsWith("B", WallHardwareMap.GetRelayLabel(7));
            Assert.StartsWith("C", WallHardwareMap.GetRelayLabel(14));
            Assert.StartsWith("D", WallHardwareMap.GetRelayLabel(21));
            Assert.StartsWith("E", WallHardwareMap.GetRelayLabel(28));
        }

        [Fact]
        public void ColumnNumbersOnTheStickersCountFromOne()
        {
            // The sticker says C4, and that is column index 3 in the code -
            // matching col4On() touching lights[r][3] in the original sketch.
            Assert.True(WallHardwareMap.TryParseRelayLabel("C4", out int bulbIndex));

            (int row, int column) = WallHardwareMap.GetPosition(bulbIndex);

            Assert.Equal(2, row);      // C
            Assert.Equal(3, column);   // 4 counted from 1
        }

        [Fact]
        public void EveryBulbHasItsOwnPin()
        {
            // A duplicated pin would mean two bulbs always lighting together,
            // which would be baffling on the wall and is trivial to catch here.
            var pins = new List<int>();

            for (int i = 0; i < WallHardwareMap.BulbCount; i++)
            {
                pins.Add(WallHardwareMap.GetArduinoPin(i));
            }

            Assert.Equal(35, pins.Distinct().Count());
        }

        [Fact]
        public void EveryBulbHasItsOwnLabel()
        {
            var labels = new List<string>();

            for (int i = 0; i < WallHardwareMap.BulbCount; i++)
            {
                labels.Add(WallHardwareMap.GetRelayLabel(i));
            }

            Assert.Equal(35, labels.Distinct().Count());
        }

        [Fact]
        public void NoPinClashesWithTheSerialPort()
        {
            // Pins 0 and 1 are RX0 and TX0 on a Mega, shared with the USB
            // connection. If the wall used either, talking to the board would
            // break the wall and vice versa.
            //
            // The original sketch never used serial, so this was never tested in
            // practice. It is about to be.
            for (int i = 0; i < WallHardwareMap.BulbCount; i++)
            {
                int pin = WallHardwareMap.GetArduinoPin(i);

                Assert.True(pin >= 2, $"Bulb {i} uses pin {pin}, which collides with the serial port.");
            }
        }

        [Fact]
        public void LabelsAndPositionsSurviveARoundTrip()
        {
            for (int i = 0; i < WallHardwareMap.BulbCount; i++)
            {
                string label = WallHardwareMap.GetRelayLabel(i);

                Assert.True(WallHardwareMap.TryParseRelayLabel(label, out int parsed));
                Assert.Equal(i, parsed);

                (int row, int column) = WallHardwareMap.GetPosition(i);
                Assert.Equal(i, WallHardwareMap.GetBulbIndex(row, column));
            }
        }

        [Theory]
        [InlineData("c4")]     // lower case
        [InlineData(" C4 ")]   // surrounding spaces
        [InlineData("C4")]
        public void LabelParsing_ForgivesTypingHabits(string typed)
        {
            // Typed by someone standing at a wall in the sun, one-handed.
            Assert.True(WallHardwareMap.TryParseRelayLabel(typed, out int bulbIndex));
            Assert.Equal(17, bulbIndex);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("F1")]     // no row F
        [InlineData("A0")]     // columns start at 1
        [InlineData("A8")]     // only 7 columns
        [InlineData("A")]      // too short
        [InlineData("A12")]    // too long
        [InlineData("11")]     // not a row letter
        public void LabelParsing_RejectsNonsenseWithoutThrowing(string? typed)
        {
            // A typo is an ordinary thing to expect here, not a programming
            // error, so this reports failure rather than throwing.
            Assert.False(WallHardwareMap.TryParseRelayLabel(typed, out int bulbIndex));
            Assert.Equal(-1, bulbIndex);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(35)]
        public void BulbNumbersOutsideTheWallAreRejected(int bulbIndex)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => WallHardwareMap.GetRelayLabel(bulbIndex));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => WallHardwareMap.GetArduinoPin(bulbIndex));
        }

        [Fact]
        public void Describe_NamesTheBulbEveryWayAtOnce()
        {
            // All four names have to appear, because the whole point of the
            // readout is cross-checking them against each other at the wall.
            string described = WallHardwareMap.Describe(17);

            Assert.Contains("17", described);
            Assert.Contains("C4", described);
            Assert.Contains("27", described);
        }

        // ------------------------------------------------------------------
        // The identify effect itself
        // ------------------------------------------------------------------

        [Fact]
        public void IdentifyEffect_LightsExactlyOneBulb()
        {
            var effect = new BulbIdentifyEffect();

            for (int i = 0; i < WallHardwareMap.BulbCount; i++)
            {
                var parameters = new EffectParameters { IdentifyBulbIndex = i };
                var context = new EffectContext(0.0, parameters, sessionSeed: 1);

                var frame = new WallFrame();
                effect.Render(context, frame);

                Assert.Equal(1, frame.CountLitCells());

                (int row, int column) = WallHardwareMap.GetPosition(i);
                Assert.True(frame.GetCell(row, column), $"Bulb {i} lit the wrong cell.");
            }
        }

        [Fact]
        public void IdentifyEffect_LeavesTheWallDarkForAnImpossibleBulb()
        {
            // Used while somebody is up a ladder. A bad value should leave the
            // wall dark, not take the app down.
            var effect = new BulbIdentifyEffect();

            foreach (int bad in new[] { -1, 35, 999 })
            {
                var parameters = new EffectParameters { IdentifyBulbIndex = bad };
                var context = new EffectContext(0.0, parameters, sessionSeed: 1);

                var frame = new WallFrame();
                effect.Render(context, frame);

                Assert.Equal(0, frame.CountLitCells());
            }
        }

        [Fact]
        public void IdentifyEffect_HoldsStillOverTime()
        {
            // It must not drift while somebody walks round the wall to look.
            var effect = new BulbIdentifyEffect();
            var parameters = new EffectParameters { IdentifyBulbIndex = 20 };

            var atStart = new WallFrame();
            effect.Render(new EffectContext(0.0, parameters, 1), atStart);

            var muchLater = new WallFrame();
            effect.Render(new EffectContext(300.0, parameters, 1), muchLater);

            Assert.True(atStart.ContentEquals(muchLater));
        }
    }
}
