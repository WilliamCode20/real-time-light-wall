using System.Collections.Generic;
using LightWall.Core.Animations;
using LightWall.Core.Effects;
using LightWall.Core.Models;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the effect system.
    ///
    /// The most valuable idea checked in this file is that an effect is a pure
    /// function of time: the same moment always produces the same picture.
    ///
    /// That property is what makes the whole time-based design work. It is what
    /// stops random effects dissolving into flicker when the screen redraws
    /// faster than they change, and it is what would later allow scrubbing back
    /// and forth along a show timeline.
    /// </summary>
    public class EffectTests
    {
        /// <summary>
        /// Helper that renders an effect at a given moment and hands back the
        /// resulting frame.
        /// </summary>
        private static WallFrame RenderAt(
            IWallEffect effect,
            double timeSeconds,
            int sessionSeed = 1234,
            int meteorTailLength = 3)
        {
            var parameters = new EffectParameters { MeteorTailLength = meteorTailLength };
            var context = new EffectContext(timeSeconds, parameters, sessionSeed);

            var frame = new WallFrame();
            effect.Render(context, frame);
            return frame;
        }

        /// <summary>
        /// Every effect must produce the same picture when asked about the same
        /// moment twice.
        ///
        /// This runs against the whole catalog rather than a hand-picked few, so
        /// any effect added later is covered automatically - including one whose
        /// author forgot about this rule.
        /// </summary>
        [Fact]
        public void EveryEffect_IsRepeatableAtTheSameMoment()
        {
            var catalog = new EffectCatalog();

            foreach (IWallEffect effect in catalog.AllEffects)
            {
                WallFrame first = RenderAt(effect, 1.75);
                WallFrame second = RenderAt(effect, 1.75);

                Assert.True(
                    first.ContentEquals(second),
                    $"Effect '{effect.DisplayName}' produced two different frames for the same moment.");
            }
        }

        /// <summary>
        /// Effects must wipe the frame they are handed rather than drawing on
        /// top of whatever was left in it.
        ///
        /// The engine reuses one frame object forever to avoid creating rubbish
        /// sixty times a second. The cost of that is this obligation. An effect
        /// that only turns bulbs on would leave old ones stuck lit, and the wall
        /// would gradually fill up.
        /// </summary>
        [Fact]
        public void EveryEffect_ClearsWhateverWasInTheFrameAlready()
        {
            var catalog = new EffectCatalog();
            var parameters = new EffectParameters();

            foreach (IWallEffect effect in catalog.AllEffects)
            {
                var context = new EffectContext(0.4, parameters, sessionSeed: 99);

                // Render into a blank frame.
                var fromBlank = new WallFrame();
                effect.Render(context, fromBlank);

                // Render the same moment into a frame that starts fully lit.
                var fromFull = new WallFrame();
                fromFull.Fill();
                effect.Render(context, fromFull);

                Assert.True(
                    fromBlank.ContentEquals(fromFull),
                    $"Effect '{effect.DisplayName}' left content behind from the previous frame.");
            }
        }

        [Fact]
        public void EveryEffect_HasAUsableNameAndDescription()
        {
            // These strings are shown in the interface and are meant to be read
            // by someone who did not write the code, so an empty one is a bug.
            var catalog = new EffectCatalog();

            foreach (IWallEffect effect in catalog.AllEffects)
            {
                Assert.False(string.IsNullOrWhiteSpace(effect.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(effect.Description));
            }
        }

        [Fact]
        public void Catalog_ContainsAllTheExpectedEffects()
        {
            var catalog = new EffectCatalog();

            Assert.Equal(9, catalog.StaticPatterns.Count);
            Assert.Equal(3, catalog.SequenceAnimations.Count);
            Assert.Equal(5, catalog.ProceduralAnimations.Count);
            Assert.Single(catalog.Diagnostics);
            Assert.Equal(18, catalog.AllEffects.Count);
        }

        [Fact]
        public void Catalog_FindsEffectsByNameIgnoringCase()
        {
            var catalog = new EffectCatalog();

            Assert.NotNull(catalog.FindByName("Meteor"));
            Assert.NotNull(catalog.FindByName("meteor"));
            Assert.Null(catalog.FindByName("No Such Effect"));
        }

        [Fact]
        public void ClearEffect_ProducesADarkWall()
        {
            var catalog = new EffectCatalog();
            IWallEffect clear = catalog.FindByName("Clear")!;

            Assert.Equal(0, RenderAt(clear, 0.0).CountLitCells());
        }

        [Fact]
        public void FillEffect_LightsEveryBulb()
        {
            var catalog = new EffectCatalog();
            IWallEffect fill = catalog.FindByName("Fill")!;

            Assert.Equal(35, RenderAt(fill, 0.0).CountLitCells());
        }

        /// <summary>
        /// A still pattern must stay still even though it is redrawn constantly.
        ///
        /// Sparkle is the interesting case: it uses random numbers, but is meant
        /// to hold one arrangement rather than shimmer. It manages that by
        /// always asking for step 0's randomness.
        /// </summary>
        [Fact]
        public void SparklePattern_HoldsStillAcrossTime()
        {
            var catalog = new EffectCatalog();
            IWallEffect sparkle = catalog.FindByName("Sparkle")!;

            WallFrame atStart = RenderAt(sparkle, 0.0, sessionSeed: 7);
            WallFrame muchLater = RenderAt(sparkle, 45.0, sessionSeed: 7);

            Assert.True(atStart.ContentEquals(muchLater));
        }

        [Fact]
        public void SparklePattern_DiffersBetweenRuns()
        {
            // A different session seed represents pressing the button again,
            // which should give a fresh arrangement.
            var catalog = new EffectCatalog();
            IWallEffect sparkle = catalog.FindByName("Sparkle")!;

            WallFrame firstPress = RenderAt(sparkle, 0.0, sessionSeed: 1);
            WallFrame secondPress = RenderAt(sparkle, 0.0, sessionSeed: 2);

            Assert.False(firstPress.ContentEquals(secondPress));
        }

        [Fact]
        public void FrameSequence_AdvancesAtItsStatedRate()
        {
            // Three easily distinguishable frames: one lit bulb in each row 0, 1
            // and 2 respectively.
            var frames = new List<WallFrame>();

            for (int row = 0; row < 3; row++)
            {
                var frame = new WallFrame();
                frame.SetCell(row, 0, true);
                frames.Add(frame);
            }

            var effect = new FrameSequenceEffect("Test", "Test sequence", frames, framesPerSecond: 10.0);

            // At 10 frames a second, each frame lasts a tenth of a second.
            Assert.True(RenderAt(effect, 0.00).GetCell(0, 0));
            Assert.True(RenderAt(effect, 0.05).GetCell(0, 0));  // still frame 0
            Assert.True(RenderAt(effect, 0.10).GetCell(1, 0));  // now frame 1
            Assert.True(RenderAt(effect, 0.20).GetCell(2, 0));  // now frame 2
        }

        [Fact]
        public void FrameSequence_LoopsBackToTheStart()
        {
            var frames = new List<WallFrame>();

            for (int row = 0; row < 3; row++)
            {
                var frame = new WallFrame();
                frame.SetCell(row, 0, true);
                frames.Add(frame);
            }

            var effect = new FrameSequenceEffect("Test", "Test sequence", frames, framesPerSecond: 10.0);

            // Frame 3 does not exist, so playback should wrap round to frame 0.
            Assert.True(RenderAt(effect, 0.30).GetCell(0, 0));
        }

        [Fact]
        public void Meteor_TailLengthControlsHowManyBulbsLight()
        {
            var meteor = new MeteorEffect();

            // Pick a moment where the meteor sits well clear of both edges, so
            // the whole tail fits on the wall and nothing is clipped.
            //
            // At 8 steps a second, 0.625 seconds is step 5. With the head at
            // column 5, a tail of 3 covers columns 5, 4 and 3.
            WallFrame shortTail = RenderAt(meteor, 0.625, meteorTailLength: 1);
            WallFrame longTail = RenderAt(meteor, 0.625, meteorTailLength: 3);

            Assert.Equal(1, shortTail.CountLitCells());
            Assert.Equal(3, longTail.CountLitCells());
        }

        [Fact]
        public void Meteor_TravelsAcrossASingleRow()
        {
            var meteor = new MeteorEffect();

            WallFrame frame = RenderAt(meteor, 0.625, meteorTailLength: 3);

            // Every lit bulb should share one row, because the meteor sweeps
            // horizontally rather than diagonally.
            var litRows = new HashSet<int>();

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    if (frame.GetCell(row, column))
                    {
                        litRows.Add(row);
                    }
                }
            }

            Assert.Single(litRows);
        }

        [Fact]
        public void EqBumper_AlwaysLightsTheBottomRow()
        {
            // Every bar is at least one cell tall, which keeps a visible floor
            // for the bars to stand on. Losing that would make the effect look
            // broken at quiet moments.
            var eqBumper = new EqBumperEffect();

            for (double time = 0.0; time < 3.0; time += 0.1)
            {
                WallFrame frame = RenderAt(eqBumper, time);

                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    Assert.True(
                        frame.GetCell(WallFrame.Rows - 1, column),
                        $"Bottom row was dark at column {column}, time {time:F1}s.");
                }
            }
        }

        [Fact]
        public void EqBumper_BarsAreSolidFromTheBottomUp()
        {
            // A bar must be a continuous column of lit cells rising from the
            // bottom, with no gaps. A gap would mean floating cells, which is
            // not what an equaliser bar looks like.
            var eqBumper = new EqBumperEffect();

            for (double time = 0.0; time < 2.0; time += 0.1)
            {
                WallFrame frame = RenderAt(eqBumper, time);

                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    bool foundGap = false;

                    // Walk upward from the bottom. Once we meet an unlit cell,
                    // every cell above it must also be unlit.
                    for (int row = WallFrame.Rows - 1; row >= 0; row--)
                    {
                        if (!frame.GetCell(row, column))
                        {
                            foundGap = true;
                        }
                        else if (foundGap)
                        {
                            Assert.Fail(
                                $"Column {column} had a floating lit cell above a gap at time {time:F1}s.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The spiral path must visit all 35 bulbs exactly once.
        ///
        /// The spiral is built by walking inward while shrinking four boundaries,
        /// which is the sort of loop where an off-by-one either skips a cell or
        /// visits one twice. Neither is obvious on screen - a missed centre cell
        /// in a fast spiral just looks like a flicker - so it is worth checking
        /// properly.
        /// </summary>
        [Fact]
        public void SpiralSequence_LightsEveryBulbExactlyOnceOnTheWayIn()
        {
            List<WallFrame> frames = WallAnimations.CreateSpiralInOutFrames();

            // The inward half adds one bulb per frame, so after 35 frames the
            // wall should be completely lit.
            Assert.True(frames.Count >= 35);
            Assert.Equal(35, frames[34].CountLitCells());

            // And each earlier frame should have exactly one bulb fewer,
            // confirming the path never revisits a cell.
            for (int i = 0; i < 35; i++)
            {
                Assert.Equal(i + 1, frames[i].CountLitCells());
            }
        }

        [Fact]
        public void SpiralSequence_EndsCompletelyDark()
        {
            List<WallFrame> frames = WallAnimations.CreateSpiralInOutFrames();

            Assert.Equal(0, frames[^1].CountLitCells());
        }

        [Fact]
        public void RowSweep_LightsExactlyOneRowInEveryFrame()
        {
            List<WallFrame> frames = WallAnimations.CreateRowSweepFrames();

            foreach (WallFrame frame in frames)
            {
                Assert.Equal(WallFrame.Columns, frame.CountLitCells());
            }
        }
    }
}
