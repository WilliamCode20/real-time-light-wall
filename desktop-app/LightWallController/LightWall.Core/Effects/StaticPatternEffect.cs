using System;
using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Turns a still, non-moving pattern into an effect.
    ///
    /// Checkerboard, Border, Cross and friends do not animate - they are a
    /// single fixed arrangement. This class wraps one of those so it can be
    /// treated exactly like an animated effect by the rest of the app.
    ///
    /// WHY BOTHER WRAPPING SOMETHING THAT DOES NOT MOVE?
    ///
    /// So that the engine always has exactly one answer to the question "what is
    /// playing right now?". Without this, the serial layer and the audio layer
    /// would each need a special case for "actually nothing is playing, there is
    /// just a static picture sitting there".
    ///
    /// Making stillness just another kind of effect removes that special case
    /// everywhere, at the cost of this one small class.
    ///
    /// HOW RANDOM PATTERNS STAY STILL
    ///
    /// Sparkle and Randomize use random numbers, but they are still meant to
    /// hold one arrangement rather than shimmer constantly.
    ///
    /// They stay still because we always ask for the randomness belonging to
    /// step 0, and step 0's randomness never changes while an effect is active.
    /// So redrawing reproduces the identical arrangement.
    ///
    /// Selecting the effect again starts a new session with a new seed, which is
    /// why clicking Sparkle twice gives two different sparkle patterns - exactly
    /// how it behaved before.
    /// </summary>
    public sealed class StaticPatternEffect : IWallEffect
    {
        /// <summary>
        /// The actual drawing routine, handed in when this effect is created.
        ///
        /// Storing a method in a variable like this is what C# calls a
        /// "delegate". It lets us build nine different static-pattern effects
        /// from this one class instead of writing nine nearly identical classes.
        /// </summary>
        private readonly Action<WallFrame, Random> _applyPattern;

        /// <summary>
        /// Creates a still-pattern effect from a drawing routine.
        /// </summary>
        /// <param name="displayName">The name a human sees, e.g. "Checkerboard".</param>
        /// <param name="description">A short explanation for tooltips and menus.</param>
        /// <param name="applyPattern">
        /// The routine that draws the pattern. It receives the frame to draw
        /// into and a random generator. Patterns that are not random simply
        /// ignore the generator.
        /// </param>
        public StaticPatternEffect(
            string displayName,
            string description,
            Action<WallFrame, Random> applyPattern)
        {
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            _applyPattern = applyPattern ?? throw new ArgumentNullException(nameof(applyPattern));
        }

        /// <inheritdoc />
        public string DisplayName { get; }

        /// <inheritdoc />
        public string Description { get; }

        /// <inheritdoc />
        public void Render(EffectContext context, WallFrame target)
        {
            // Always use step 0's randomness so the arrangement is identical on
            // every redraw. See the class comment above for why this matters.
            Random random = context.CreateRandomForStep(0);

            _applyPattern(target, random);
        }
    }
}
