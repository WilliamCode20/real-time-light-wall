using System;
using System.Collections.Generic;
using System.Text;
using LightWall.Core.Models;
using LightWall.Core.Patterns;

namespace LightWall.Core.Animations
{
    /// <summary>
    /// Contains helper methods that create frame-by-frame animation sequences.
    ///
    /// Important distinction:
    /// - WallPatterns builds ONE frame arrangement
    /// - WallAnimations builds MANY frames in order
    ///
    /// These methods do not run timers themselves.
    /// They just return lists of WallFrame objects.
    ///
    /// The UI layer (MainWindow) is responsible for:
    /// - taking those frames
    /// - stepping through them over time
    /// - rendering them on the simulator
    /// </summary>
    public static class WallAnimations
    {
        /// <summary>
        /// Creates a simple row sweep animation.
        ///
        /// Sequence:
        /// - light row 0
        /// - light row 1
        /// - light row 2
        /// - light row 3
        /// - light row 4
        /// - then move back inward/upward without duplicating the endpoints
        ///
        /// This gives a bouncing sweep feeling rather than restarting harshly.
        /// </summary>
        public static List<WallFrame> CreateRowSweepFrames()
        {
            var frames = new List<WallFrame>();

            // Sweep downward through all rows.
            for (int row = 0; row < WallFrame.Rows; row++)
            {
                var frame = new WallFrame();
                frame.SetRow(row, true);
                frames.Add(frame);
            }

            // Sweep back upward, but avoid repeating the very bottom and top rows
            // immediately, which would create a slightly awkward double-frame pause.
            for (int row = WallFrame.Rows - 2; row >= 1; row--)
            {
                var frame = new WallFrame();
                frame.SetRow(row, true);
                frames.Add(frame);
            }

            return frames;
        }

        /// <summary>
        /// Creates a simple inward/outward border pulse sequence.
        ///
        /// Sequence:
        /// 1. outer border
        /// 2. cross
        /// 3. center cell
        /// 4. cross again
        /// 5. border again
        ///
        /// This gives a very stylized "pulse inward, then back out" effect.
        /// </summary>
        public static List<WallFrame> CreateBorderPulseFrames()
        {
            var frames = new List<WallFrame>();

            // Frame 1: full outer border.
            var outerBorder = new WallFrame();
            WallPatterns.ApplyBorder(outerBorder);
            frames.Add(outerBorder);

            // Frame 2: collapse inward to a cross.
            var cross = new WallFrame();
            WallPatterns.ApplyCross(cross);
            frames.Add(cross);

            // Frame 3: collapse further to just the center light.
            var center = new WallFrame();
            center.SetCell(WallFrame.Rows / 2, WallFrame.Columns / 2, true);
            frames.Add(center);

            // Frame 4: expand back to the cross.
            var crossReturn = new WallFrame();
            WallPatterns.ApplyCross(crossReturn);
            frames.Add(crossReturn);

            // Frame 5: expand back to the full border.
            var borderReturn = new WallFrame();
            WallPatterns.ApplyBorder(borderReturn);
            frames.Add(borderReturn);

            return frames;
        }
    }
}