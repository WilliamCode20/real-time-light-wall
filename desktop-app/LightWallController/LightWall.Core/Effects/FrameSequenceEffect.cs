using System;
using System.Collections.Generic;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Plays a prepared list of frames in order, on a loop, like a flipbook.
    ///
    /// Row Sweep, Border Pulse and Spiral all work this way: the whole sequence
    /// is worked out in advance, and playback just walks through it.
    ///
    /// This single class replaces what used to be three separate playback paths.
    /// The frame lists themselves are still built by WallAnimations - that code
    /// did not need to change, it just needed a consistent way to be played.
    ///
    /// HOW TIME TURNS INTO A FRAME NUMBER
    ///
    /// Each sequence has its own natural pace, given as frames per second.
    /// Row Sweep looks right at about 5.5 frames a second; Spiral wants a
    /// brisker 8.3.
    ///
    /// To find the frame for a moment in time we multiply time by that pace:
    ///
    ///   2.0 seconds x 5.5 frames per second = frame 11
    ///
    /// then wrap around with the remainder operator so the sequence loops
    /// forever. Frame 11 of an 8-frame sequence becomes frame 3.
    ///
    /// The pleasant consequence is that the sequence advances at its own pace no
    /// matter how often the screen redraws. Redrawing 60 times a second does not
    /// make a 5.5-frames-per-second sweep run eleven times too fast; it just
    /// means the same frame is drawn several times in a row, which is invisible
    /// and harmless.
    /// </summary>
    public sealed class FrameSequenceEffect : IWallEffect
    {
        /// <summary>
        /// The prepared frames, in playback order.
        /// </summary>
        private readonly IReadOnlyList<WallFrame> _frames;

        /// <summary>
        /// How quickly to advance through the list, in frames per second.
        /// </summary>
        private readonly double _framesPerSecond;

        /// <summary>
        /// Creates a flipbook-style effect from a prepared list of frames.
        /// </summary>
        /// <param name="displayName">The name a human sees, e.g. "Row Sweep".</param>
        /// <param name="description">A short explanation for tooltips and menus.</param>
        /// <param name="frames">
        /// The frames to play, in order. Must contain at least one frame.
        /// </param>
        /// <param name="framesPerSecond">
        /// The sequence's natural pace. This is the speed at 100% on the speed
        /// slider; the slider scales it from there.
        /// </param>
        public FrameSequenceEffect(
            string displayName,
            string description,
            IReadOnlyList<WallFrame> frames,
            double framesPerSecond)
        {
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));

            if (frames.Count == 0)
            {
                throw new ArgumentException(
                    "A frame sequence needs at least one frame.",
                    nameof(frames));
            }

            if (framesPerSecond <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(framesPerSecond),
                    "Frames per second must be greater than zero.");
            }

            _framesPerSecond = framesPerSecond;
        }

        /// <inheritdoc />
        public string DisplayName { get; }

        /// <inheritdoc />
        public string Description { get; }

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            // Work out how many frames we should have advanced by now.
            int rawFrameNumber = context.GetStep(_framesPerSecond);

            // Wrap around so the sequence repeats forever.
            //
            // The Math.Abs guards against a negative time value producing a
            // negative index, which would crash. Time should never be negative
            // in normal use, but an index-out-of-range crash is a harsh penalty
            // for an assumption that costs nothing to defend against.
            int frameIndex = Math.Abs(rawFrameNumber) % _frames.Count;

            target.CopyFrom(_frames[frameIndex]);
        }
    }
}
