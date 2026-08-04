using System;
using LightWall.Core.Models;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for WallFrame, the model holding which of the 35 bulbs are lit.
    ///
    /// HOW A TEST WORKS, IF THIS IS NEW
    ///
    /// Each method marked [Fact] is one test. It sets up a situation, performs
    /// an action, and then asserts what should be true. If an assertion is
    /// wrong, that test fails and names itself in the output.
    ///
    /// Run them all with:
    ///   dotnet test
    ///
    /// The point of these is not that WallFrame is complicated. It is that
    /// everything else in the app is built on top of it, so a mistake here would
    /// show up as a baffling problem somewhere far away - most likely as the
    /// physical wall doing something strange, which is the most expensive place
    /// to debug anything.
    /// </summary>
    public class WallFrameTests
    {
        [Fact]
        public void NewFrame_StartsCompletelyDark()
        {
            var frame = new WallFrame();

            Assert.Equal(0, frame.CountLitCells());
        }

        [Fact]
        public void SetCell_ChangesOnlyThatCell()
        {
            var frame = new WallFrame();

            frame.SetCell(2, 3, true);

            Assert.True(frame.GetCell(2, 3));
            Assert.Equal(1, frame.CountLitCells());
        }

        [Fact]
        public void ToggleCell_FlipsBackAndForth()
        {
            var frame = new WallFrame();

            frame.ToggleCell(1, 1);
            Assert.True(frame.GetCell(1, 1));

            frame.ToggleCell(1, 1);
            Assert.False(frame.GetCell(1, 1));
        }

        [Fact]
        public void Fill_LightsAllThirtyFiveBulbs()
        {
            var frame = new WallFrame();

            frame.Fill();

            Assert.Equal(35, frame.CountLitCells());
        }

        [Fact]
        public void Clear_TurnsEverythingOff()
        {
            var frame = new WallFrame();
            frame.Fill();

            frame.Clear();

            Assert.Equal(0, frame.CountLitCells());
        }

        [Fact]
        public void SetRow_LightsExactlySevenBulbs()
        {
            var frame = new WallFrame();

            frame.SetRow(2, true);

            Assert.Equal(WallFrame.Columns, frame.CountLitCells());

            for (int column = 0; column < WallFrame.Columns; column++)
            {
                Assert.True(frame.GetCell(2, column));
            }
        }

        [Fact]
        public void SetColumn_LightsExactlyFiveBulbs()
        {
            var frame = new WallFrame();

            frame.SetColumn(3, true);

            Assert.Equal(WallFrame.Rows, frame.CountLitCells());

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                Assert.True(frame.GetCell(row, 3));
            }
        }

        [Fact]
        public void CopyFrom_ReproducesTheOtherFrameExactly()
        {
            var source = new WallFrame();
            source.SetCell(0, 0, true);
            source.SetCell(4, 6, true);

            var destination = new WallFrame();

            // Pre-load the destination with something different, to prove the
            // copy replaces the contents rather than merging into them.
            destination.SetCell(2, 2, true);

            destination.CopyFrom(source);

            Assert.True(destination.ContentEquals(source));
            Assert.False(destination.GetCell(2, 2));
        }

        [Fact]
        public void CreateTranslated_MovesContentDownAndRight()
        {
            var frame = new WallFrame();
            frame.SetCell(0, 0, true);

            // Positive offsets move down and to the right.
            WallFrame moved = frame.CreateTranslated(rowOffset: 1, columnOffset: 2);

            Assert.False(moved.GetCell(0, 0));
            Assert.True(moved.GetCell(1, 2));
            Assert.Equal(1, moved.CountLitCells());
        }

        [Fact]
        public void CreateTranslated_LeavesTheOriginalUntouched()
        {
            var frame = new WallFrame();
            frame.SetCell(0, 0, true);

            frame.CreateTranslated(2, 2);

            // The original should still show what it always did.
            Assert.True(frame.GetCell(0, 0));
            Assert.Equal(1, frame.CountLitCells());
        }

        /// <summary>
        /// Content shifted beyond an edge is discarded rather than wrapping
        /// round to the opposite side.
        ///
        /// This is checked on all four edges because an off-by-one in the bounds
        /// check would typically only break one of them, and would be easy to
        /// miss if only one direction were tested.
        /// </summary>
        [Theory]
        [InlineData(-1, 0)]  // pushed off the top
        [InlineData(1, 0)]   // pushed off the bottom
        [InlineData(0, -1)]  // pushed off the left
        [InlineData(0, 1)]   // pushed off the right
        public void CreateTranslated_DiscardsContentPushedOffTheEdge(int rowOffset, int columnOffset)
        {
            var frame = new WallFrame();

            // Light the single cell that the given offset will push outside.
            int row = rowOffset < 0 ? 0 : WallFrame.Rows - 1;
            int column = columnOffset < 0 ? 0 : WallFrame.Columns - 1;
            frame.SetCell(row, column, true);

            WallFrame moved = frame.CreateTranslated(rowOffset, columnOffset);

            Assert.Equal(0, moved.CountLitCells());
        }

        [Fact]
        public void CopyTranslatedFrom_MatchesCreateTranslated()
        {
            // These two methods must agree. CreateTranslated is the readable
            // one; CopyTranslatedFrom is the one the engine actually uses every
            // frame because it avoids creating a new object each time. If they
            // ever drifted apart, the wall and the tests would disagree.
            var source = new WallFrame();
            source.SetCell(1, 1, true);
            source.SetCell(3, 5, true);

            WallFrame expected = source.CreateTranslated(1, -1);

            var actual = new WallFrame();
            actual.CopyTranslatedFrom(source, 1, -1);

            Assert.True(actual.ContentEquals(expected));
        }

        [Fact]
        public void CopyTranslatedFrom_ClearsWhateverWasThereBefore()
        {
            var source = new WallFrame();
            source.SetCell(0, 0, true);

            var destination = new WallFrame();
            destination.Fill();

            destination.CopyTranslatedFrom(source, 0, 0);

            // Only the one cell from the source should survive; the fill must be
            // wiped out rather than shining through.
            Assert.Equal(1, destination.CountLitCells());
            Assert.True(destination.GetCell(0, 0));
        }

        [Fact]
        public void ContentEquals_DistinguishesDifferentFrames()
        {
            var first = new WallFrame();
            var second = new WallFrame();

            Assert.True(first.ContentEquals(second));

            second.SetCell(2, 2, true);

            Assert.False(first.ContentEquals(second));
        }

        /// <summary>
        /// Asking for a cell outside the 5x7 wall should fail loudly and
        /// immediately.
        ///
        /// A quiet failure here would be much worse. Silently ignoring a bad
        /// coordinate would let a broken effect look almost right, and the
        /// mistake would only surface much later as a bulb that never lights.
        /// </summary>
        [Theory]
        [InlineData(-1, 0)]
        [InlineData(5, 0)]
        [InlineData(0, -1)]
        [InlineData(0, 7)]
        public void GetCell_RejectsCoordinatesOutsideTheWall(int row, int column)
        {
            var frame = new WallFrame();

            Assert.Throws<ArgumentOutOfRangeException>(() => frame.GetCell(row, column));
        }

        [Fact]
        public void Randomize_WithTheSameSeed_ProducesTheSameArrangement()
        {
            // Handing in the random generator rather than creating one inside
            // means the caller controls it, which is what makes random behaviour
            // testable at all.
            var first = new WallFrame();
            var second = new WallFrame();

            first.Randomize(new Random(12345));
            second.Randomize(new Random(12345));

            Assert.True(first.ContentEquals(second));
        }
    }
}
