using LightWall.Core.Effects;
using LightWall.Core.Engine;
using LightWall.Core.Models;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for WallEngine, which decides what the wall shows at any moment.
    ///
    /// These are only possible because the engine was moved out of the window.
    /// While that logic lived in MainWindow, testing it would have meant opening
    /// a window and clicking things, which is slow, awkward and unreliable.
    /// Now it is an ordinary object and can simply be asked questions.
    /// </summary>
    public class WallEngineTests
    {
        /// <summary>
        /// A deliberately simple effect used for testing.
        ///
        /// It lights a single bulb that moves one column to the right per
        /// second, which makes "how far has time advanced?" directly visible as
        /// a position on the wall.
        /// </summary>
        private sealed class MarchingCellEffect : IWallEffect
        {
            public string DisplayName => "Marching Cell";

            public string Description => "One bulb that moves right by one column per second.";

            public void Render(EffectContext context, WallFrame target)
            {
                target.Clear();

                int column = context.GetStep(1.0) % WallFrame.Columns;
                target.SetCell(0, column, true);
            }
        }

        /// <summary>
        /// Advances the engine by a stretch of time in many small steps, the
        /// way the real timer does.
        ///
        /// WHY THIS HELPER IS NEEDED
        ///
        /// The engine refuses to accept a single enormous time step, capping it
        /// at a quarter of a second. That guard exists so a pause at a debugger
        /// breakpoint cannot make an animation leap somewhere unrelated.
        ///
        /// It does mean a test cannot skip forward three seconds in one call -
        /// it would be capped just like a real stall would be. Feeding in a
        /// stream of small steps is both what really happens sixty times a
        /// second, and the only way to cover real ground.
        /// </summary>
        private static void AdvanceBy(WallEngine engine, double totalSeconds, double stepSeconds = 0.01)
        {
            int steps = (int)System.Math.Round(totalSeconds / stepSeconds);

            for (int i = 0; i < steps; i++)
            {
                engine.Advance(stepSeconds);
            }
        }

        [Fact]
        public void NewEngine_StartsInManualModeWithADarkWall()
        {
            var engine = new WallEngine();

            Assert.False(engine.IsPlaying);
            Assert.Null(engine.ActiveEffect);
            Assert.Equal(0, engine.CurrentFrame.CountLitCells());
        }

        [Fact]
        public void Play_DrawsTheFirstFrameStraightAway()
        {
            // Waiting for the next timer tick would make button presses feel
            // sluggish, so Play renders immediately.
            var engine = new WallEngine();

            engine.Play(new MarchingCellEffect());

            Assert.True(engine.IsPlaying);
            Assert.True(engine.CurrentFrame.GetCell(0, 0));
        }

        [Fact]
        public void Advance_MovesTheEffectForwardInTime()
        {
            var engine = new WallEngine();
            engine.Play(new MarchingCellEffect());

            AdvanceBy(engine, 2.5);

            // Two and a half seconds at one column per second puts the bulb in
            // column 2, since the step only advances on whole seconds.
            //
            // Note the deliberate half-second. Landing exactly on 2.0 would be
            // asking the test to sit precisely on the boundary between columns 1
            // and 2. Adding up three hundred small steps lands a whisker either
            // side of a round number rather than exactly on it, so a test poised
            // on that boundary could fall either way. Aiming for the middle of a
            // step tests the same behaviour without the coin toss.
            Assert.True(engine.CurrentFrame.GetCell(0, 2));
        }

        [Fact]
        public void Advance_AddsUpSmallStepsTheSameAsOneBigStep()
        {
            // The real timer delivers many tiny increments rather than a single
            // large one, so those must produce the same result.
            var engine = new WallEngine();
            engine.Play(new MarchingCellEffect());

            for (int i = 0; i < 100; i++)
            {
                engine.Advance(0.02);
            }

            Assert.Equal(2.0, engine.EffectTimeSeconds, precision: 6);
        }

        [Fact]
        public void SpeedMultiplier_ScalesHowFastTimePasses()
        {
            var engine = new WallEngine();
            engine.Play(new MarchingCellEffect());

            engine.SpeedMultiplier = 2.0;
            AdvanceBy(engine, 1.25);

            // One and a quarter real seconds at double speed counts as two and
            // a half effect seconds, which puts the bulb in column 2.
            Assert.Equal(2.5, engine.EffectTimeSeconds, precision: 6);
            Assert.True(engine.CurrentFrame.GetCell(0, 2));
        }

        [Fact]
        public void ChangingSpeedMidAnimation_DoesNotJumpTheAnimation()
        {
            // Speed changes should adjust the pace from that point onward, not
            // recompute where the animation "should" have been by now.
            var engine = new WallEngine();
            engine.Play(new MarchingCellEffect());

            AdvanceBy(engine, 2.0);
            Assert.Equal(2.0, engine.EffectTimeSeconds, precision: 6);

            engine.SpeedMultiplier = 3.0;
            AdvanceBy(engine, 1.0);

            // 2 seconds already banked, plus 1 second at triple speed.
            Assert.Equal(5.0, engine.EffectTimeSeconds, precision: 6);
        }

        [Fact]
        public void Advance_IgnoresEnormousTimeJumps()
        {
            // A long pause at a debugger breakpoint, or a laptop waking from
            // sleep, can report that a very long time has passed. Letting that
            // through would make the animation leap somewhere unrelated.
            var engine = new WallEngine();
            engine.Play(new MarchingCellEffect());

            engine.Advance(600.0);

            // The step is capped rather than accepted in full.
            Assert.True(engine.EffectTimeSeconds <= 0.25);
        }

        [Fact]
        public void Advance_IgnoresTimeRunningBackwards()
        {
            var engine = new WallEngine();
            engine.Play(new MarchingCellEffect());

            AdvanceBy(engine, 1.0);
            engine.Advance(-5.0);

            Assert.Equal(1.0, engine.EffectTimeSeconds, precision: 6);
        }

        [Fact]
        public void Advance_DoesNothingInManualMode()
        {
            var engine = new WallEngine();

            engine.ToggleCell(2, 3);
            engine.Advance(10.0);

            // The hand-made pattern must survive; nothing should paint over it.
            Assert.True(engine.CurrentFrame.GetCell(2, 3));
            Assert.Equal(1, engine.CurrentFrame.CountLitCells());
        }

        [Fact]
        public void ToggleCell_StopsAnyRunningEffect()
        {
            // Leaving an effect playing while the user edits by hand would mean
            // their change gets painted over almost immediately.
            var engine = new WallEngine();
            engine.Play(new MarchingCellEffect());

            engine.ToggleCell(4, 4);

            Assert.False(engine.IsPlaying);
        }

        [Fact]
        public void Stop_LeavesTheCurrentFrameOnTheWall()
        {
            // Stop is for freezing a frame to look at it. Going dark is what the
            // Clear effect is for.
            var engine = new WallEngine();
            engine.Play(new MarchingCellEffect());
            AdvanceBy(engine, 3.0);

            int litBeforeStop = engine.CurrentFrame.CountLitCells();
            engine.Stop();

            Assert.False(engine.IsPlaying);
            Assert.Equal(litBeforeStop, engine.CurrentFrame.CountLitCells());
        }

        [Fact]
        public void Play_RestartsTheEffectFromTheBeginning()
        {
            var engine = new WallEngine();
            var effect = new MarchingCellEffect();

            engine.Play(effect);
            AdvanceBy(engine, 3.5);
            Assert.True(engine.CurrentFrame.GetCell(0, 3));

            engine.Play(effect);

            Assert.Equal(0.0, engine.EffectTimeSeconds);
            Assert.True(engine.CurrentFrame.GetCell(0, 0));
        }

        [Fact]
        public void Offsets_ShiftTheFinishedPicture()
        {
            var engine = new WallEngine();

            engine.OffsetRows = 2;
            engine.OffsetColumns = 1;
            engine.Play(new MarchingCellEffect());

            // The bulb the effect drew at row 0, column 0 should appear shifted
            // down two rows and right one column.
            Assert.False(engine.CurrentFrame.GetCell(0, 0));
            Assert.True(engine.CurrentFrame.GetCell(2, 1));
        }

        [Fact]
        public void Offsets_DiscardContentPushedOffTheWall()
        {
            var engine = new WallEngine();

            engine.OffsetRows = -1;
            engine.Play(new MarchingCellEffect());

            // The single bulb sat on row 0, so shifting up pushes it off the
            // top and it should simply vanish rather than wrapping round.
            Assert.Equal(0, engine.CurrentFrame.CountLitCells());
        }

        [Fact]
        public void ChangingOffsetsMidPlayback_TakesEffectOnTheNextFrame()
        {
            var engine = new WallEngine();
            engine.Play(new MarchingCellEffect());

            Assert.True(engine.CurrentFrame.GetCell(0, 0));

            engine.OffsetRows = 1;
            engine.Advance(0.01);

            Assert.True(engine.CurrentFrame.GetCell(1, 0));
        }

        [Fact]
        public void Parameters_AreReadFreshOnEveryFrame()
        {
            // This is what lets a slider change the look of an animation that is
            // already running, without restarting it.
            var engine = new WallEngine();
            engine.Play(new MeteorEffect());

            engine.Parameters.MeteorTailLength = 1;
            AdvanceBy(engine, 0.625);
            int litWithShortTail = engine.CurrentFrame.CountLitCells();

            engine.Parameters.MeteorTailLength = 4;
            engine.Advance(0.001);
            int litWithLongTail = engine.CurrentFrame.CountLitCells();

            Assert.True(
                litWithLongTail > litWithShortTail,
                "Lengthening the meteor tail mid-flight should light more bulbs.");
        }

        [Fact]
        public void SetFrameManually_ReplacesTheWallAndStopsPlayback()
        {
            var engine = new WallEngine();
            engine.Play(new MarchingCellEffect());

            var handMade = new WallFrame();
            handMade.SetRow(1, true);

            engine.SetFrameManually(handMade);

            Assert.False(engine.IsPlaying);
            Assert.Equal(WallFrame.Columns, engine.CurrentFrame.CountLitCells());
            Assert.True(engine.CurrentFrame.GetCell(1, 0));
        }
    }
}
