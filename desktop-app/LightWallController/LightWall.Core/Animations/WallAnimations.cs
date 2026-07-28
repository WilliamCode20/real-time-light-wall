using System;
using System.Collections.Generic;
using System.Text;
using LightWall.Core.Models;
using LightWall.Core.Patterns;

namespace LightWall.Core.Animations
{
    public static class WallAnimations
    {
        public static List<WallFrame> CreateRowSweepFrames()
        {
            var frames = new List<WallFrame>();

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                var frame = new WallFrame();
                frame.SetRow(row, true);
                frames.Add(frame);
            }

            for (int row = WallFrame.Rows - 2; row >= 1; row--)
            {
                var frame = new WallFrame();
                frame.SetRow(row, true);
                frames.Add(frame);
            }

            return frames;
        }

        public static List<WallFrame> CreateBorderPulseFrames()
        {
            var frames = new List<WallFrame>();

            var outerBorder = new WallFrame();
            WallPatterns.ApplyBorder(outerBorder);
            frames.Add(outerBorder);

            var cross = new WallFrame();
            WallPatterns.ApplyCross(cross);
            frames.Add(cross);

            var center = new WallFrame();
            center.SetCell(WallFrame.Rows / 2, WallFrame.Columns / 2, true);
            frames.Add(center);

            var crossReturn = new WallFrame();
            WallPatterns.ApplyCross(crossReturn);
            frames.Add(crossReturn);

            var borderReturn = new WallFrame();
            WallPatterns.ApplyBorder(borderReturn);
            frames.Add(borderReturn);

            return frames;
        }
    }
}