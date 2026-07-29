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
        /// - It has a configurable trailing tail behind it
        /// - After finishing one row, it moves to the next row
        /// - After the last row, it wraps back to the top
        ///
        /// The 'step' value controls where the meteor currently is.
        /// The 'tailLength' value controls how long the visible trail is.
        ///
        /// Why this is procedural:
        /// We are not storing every frame permanently in a list. We calculate
        /// the current frame from math based on the step number.
        /// </summary>
        public static WallFrame GenerateMeteorFrame(int step, int tailLength)
        {
            var frame = new WallFrame();

            int safeTailLength = Math.Max(1, tailLength);
            int framesPerPass = WallFrame.Columns + safeTailLength;

            // Determine which row this pass is currently using.
            int row = (step / framesPerPass) % WallFrame.Rows;

            // Determine the current head position for this step.
            int headColumn = step % framesPerPass;

            // Draw the meteor head and its trailing tail.
            for (int trailOffset = 0; trailOffset < safeTailLength; trailOffset++)
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

        /// <summary>
        /// Generates one frame of a simple EQ-like bumper animation.
        ///
        /// Behavior:
        /// - Each column behaves like a little vertical bar
        /// - Bar heights rise and fall over time
        /// - A wave-like phase offset across columns helps the wall feel
        ///   more musical and less purely random
        /// - Small random variation is added so the motion feels alive
        ///
        /// This is NOT using real audio yet. It is only simulating the
        /// visual behavior of an equalizer-style wall.
        /// </summary>
        public static WallFrame GenerateEqBumperFrame(int step, Random random)
        {
            var frame = new WallFrame();

            for (int column = 0; column < WallFrame.Columns; column++)
            {
                // Give each column a slightly different phase so the wall
                // behaves like a traveling wave rather than all bars moving together.
                double phase = (step * 0.45) + (column * 0.75);

                // Base sine-wave height from 0.0 to 1.0
                double normalized = (Math.Sin(phase) + 1.0) / 2.0;

                // Add a small amount of random jitter so it does not feel too mechanical.
                double jitter = random.NextDouble() * 0.25 - 0.125;
                double combined = Math.Clamp(normalized + jitter, 0.0, 1.0);

                // Convert to a bar height from 1 to 5 rows.
                int height = 1 + (int)Math.Round(combined * (WallFrame.Rows - 1));

                // Fill upward from the bottom row.
                for (int rowOffset = 0; rowOffset < height; rowOffset++)
                {
                    int row = WallFrame.Rows - 1 - rowOffset;
                    frame.SetCell(row, column, true);
                }
            }

            return frame;
        }
    }
}