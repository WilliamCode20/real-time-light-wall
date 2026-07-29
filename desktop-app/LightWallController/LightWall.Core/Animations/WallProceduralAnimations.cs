using System;
using System.Collections.Generic;
using System.Text;
using LightWall.Core.Models;

namespace LightWall.Core.Animations
{
    /// <summary>
    /// Contains procedural animation generators.
    ///
    /// "Procedural" means the animation is not just a fixed list of frames
    /// written out in advance. Instead, each frame is generated from rules.
    ///
    /// That makes this closer to the kind of logic we will eventually want
    /// for music-reactive behavior.
    ///
    /// Important difference:
    /// - WallAnimations -> creates prebuilt frame sequences (lists)
    /// - WallProceduralAnimations -> generates a frame for a given step number
    ///
    /// A procedural animation method usually answers the question:
    /// "At step N, what should the wall look like?"
    /// </summary>
    public static class WallProceduralAnimations
    {
        /// <summary>
        /// Generates one frame of a horizontal meteor animation.
        ///
        /// Behavior:
        /// - A "meteor" travels left-to-right across one row
        /// - It has a short trailing tail behind it
        /// - After finishing one row, it moves to the next row
        /// - After the last row, it wraps back to the top
        ///
        /// The 'step' value controls where the meteor currently is.
        ///
        /// Why this is procedural:
        /// We are not storing every frame permanently in a list. We calculate
        /// the current frame from math based on the step number.
        /// </summary>
        public static WallFrame GenerateMeteorFrame(int step)
        {
            var frame = new WallFrame();

            // How many columns the meteor head can travel through before we
            // consider the pass complete. We add a little extra space so the
            // tail can finish leaving the screen.
            const int trailLength = 3;
            int framesPerPass = WallFrame.Columns + trailLength;

            // Determine which row this pass is currently using.
            // Each full pass moves the meteor to the next row.
            int row = (step / framesPerPass) % WallFrame.Rows;

            // Determine the current head position for this step.
            int headColumn = step % framesPerPass;

            // Draw the meteor head and its trailing tail.
            // Trail cells get placed behind the head.
            for (int trailOffset = 0; trailOffset < trailLength; trailOffset++)
            {
                int column = headColumn - trailOffset;

                if (column >= 0 && column < WallFrame.Columns)
                {
                    frame.SetCell(row, column, true);
                }
            }

            return frame;
        }

        /// <summary>
        /// Generates one frame of a sparkle storm animation.
        ///
        /// Behavior:
        /// - Several random cells light up each frame
        /// - The number of sparkles gently changes over time
        /// - Every few steps, the center cell is also emphasized
        ///
        /// This is a good early example of an animation that feels more alive
        /// and less rigid than a fixed pattern.
        ///
        /// The Random object is passed in so the caller controls the shared
        /// randomness source rather than recreating new random generators.
        /// </summary>
        public static WallFrame GenerateSparkleStormFrame(int step, Random random)
        {
            var frame = new WallFrame();

            // Let the sparkle density "breathe" a little over time.
            // This gives the storm a slightly pulsing feel.
            int sparkleCount = 4 + (step % 5);

            for (int i = 0; i < sparkleCount; i++)
            {
                int row = random.Next(0, WallFrame.Rows);
                int column = random.Next(0, WallFrame.Columns);

                frame.SetCell(row, column, true);
            }

            // Add a repeating center accent every few frames.
            // This makes the animation feel a little more intentional
            // instead of being pure random noise.
            if (step % 4 == 0)
            {
                int centerRow = WallFrame.Rows / 2;
                int centerColumn = WallFrame.Columns / 2;
                frame.SetCell(centerRow, centerColumn, true);
            }

            return frame;
        }
    }
}