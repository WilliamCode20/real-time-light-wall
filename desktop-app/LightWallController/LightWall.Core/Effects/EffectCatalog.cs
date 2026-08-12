using System;
using System.Collections.Generic;
using LightWall.Core.Animations;
using LightWall.Core.Models;
using LightWall.Core.Patterns;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// The master list of every visual the wall can produce.
    ///
    /// WHY A CATALOG
    ///
    /// The window used to contain nine nearly identical button handlers, one per
    /// pattern, each repeating the same three lines with a different pattern
    /// name. Adding an effect meant editing the window layout, adding a handler,
    /// and writing the effect - three files for one idea.
    ///
    /// Now the window asks this class what exists and builds a button for each
    /// one. Adding an effect means adding a single entry below, and it appears
    /// in the interface automatically.
    ///
    /// This is also what makes the longer-term goal reachable. A DJ picking
    /// scenes from a menu needs a list of scenes to pick from, and a future
    /// audio system choosing effects to match the music needs the same thing.
    /// That list is this class.
    ///
    /// WHY IT IS NOT STATIC
    ///
    /// The frame sequences are built once when a catalog is created and then
    /// reused, rather than being rebuilt on every button press. Making this an
    /// ordinary object that gets created once keeps that straightforward, and
    /// lets tests build a throwaway catalog without disturbing anything else.
    /// </summary>
    public sealed class EffectCatalog
    {
        /// <summary>
        /// How many cells the Sparkle pattern tries to light.
        /// Some picks land on the same cell, so slightly fewer usually light up.
        /// </summary>
        private const int SparkleCellCount = 8;

        /// <summary>
        /// Builds the catalog, preparing every effect ready for use.
        /// </summary>
        public EffectCatalog()
        {
            StaticPatterns = BuildStaticPatterns();
            SequenceAnimations = BuildSequenceAnimations();
            ProceduralAnimations = BuildProceduralAnimations();
            Diagnostics = new List<IWallEffect> { new BulbIdentifyEffect() };

            var everything = new List<IWallEffect>();
            everything.AddRange(StaticPatterns);
            everything.AddRange(SequenceAnimations);
            everything.AddRange(ProceduralAnimations);
            everything.AddRange(Diagnostics);
            AllEffects = everything;
        }

        /// <summary>
        /// Still arrangements that do not move: Clear, Checkerboard, Border and
        /// so on.
        /// </summary>
        public IReadOnlyList<IWallEffect> StaticPatterns { get; }

        /// <summary>
        /// Animations played from a prepared list of frames, like a flipbook.
        /// </summary>
        public IReadOnlyList<IWallEffect> SequenceAnimations { get; }

        /// <summary>
        /// Animations worked out from arithmetic as they play, rather than
        /// stored in advance.
        /// </summary>
        public IReadOnlyList<IWallEffect> ProceduralAnimations { get; }

        /// <summary>
        /// Tools for checking the hardware rather than for putting on a show.
        ///
        /// Kept as their own list so the interface can present them separately -
        /// a DJ scrolling for something to play should not run into "Identify
        /// Bulb" between Meteor and Sparkle Storm.
        /// </summary>
        public IReadOnlyList<IWallEffect> Diagnostics { get; }

        /// <summary>
        /// Every effect in the catalog, in the order the sections appear.
        /// </summary>
        public IReadOnlyList<IWallEffect> AllEffects { get; }

        /// <summary>
        /// Finds an effect by its display name, or returns null if there is no
        /// such effect.
        ///
        /// Useful for restoring a saved selection, and later for letting audio
        /// or a saved show request an effect by name.
        /// </summary>
        public IWallEffect? FindByName(string displayName)
        {
            foreach (IWallEffect effect in AllEffects)
            {
                if (string.Equals(effect.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return effect;
                }
            }

            return null;
        }

        /// <summary>
        /// Creates the still patterns.
        ///
        /// Each entry pairs a name and description with a short routine that
        /// draws it. The simplest ones are written inline; the more involved
        /// ones call into WallPatterns, where the real drawing code lives.
        /// </summary>
        private static IReadOnlyList<IWallEffect> BuildStaticPatterns()
        {
            return new List<IWallEffect>
            {
                new StaticPatternEffect(
                    "Clear",
                    "Turns every bulb off.",
                    (frame, random) => frame.Clear()),

                new StaticPatternEffect(
                    "Fill",
                    "Turns every bulb on. Useful for checking that all 35 bulbs work.",
                    (frame, random) => frame.Fill()),

                new StaticPatternEffect(
                    "Randomize",
                    "Sets every bulb on or off at random, one arrangement per press.",
                    (frame, random) => frame.Randomize(random)),

                new StaticPatternEffect(
                    "Row 3",
                    "Lights the middle row. Labelled for humans, who count from 1, " +
                    "so this is row index 2 in the code.",
                    (frame, random) =>
                    {
                        frame.Clear();
                        frame.SetRow(2, true);
                    }),

                new StaticPatternEffect(
                    "Column 4",
                    "Lights the middle column. Human column 4 is column index 3 in " +
                    "the code.",
                    (frame, random) =>
                    {
                        frame.Clear();
                        frame.SetColumn(3, true);
                    }),

                new StaticPatternEffect(
                    "Checkerboard",
                    "Alternating bulbs on and off in a chequered grid.",
                    (frame, random) => WallPatterns.ApplyCheckerboard(frame)),

                new StaticPatternEffect(
                    "Border",
                    "Lights only the outside edge of the wall.",
                    (frame, random) => WallPatterns.ApplyBorder(frame)),

                new StaticPatternEffect(
                    "Cross",
                    "Lights the middle row and middle column, forming a plus sign.",
                    (frame, random) => WallPatterns.ApplyCross(frame)),

                new StaticPatternEffect(
                    "Sparkle",
                    "Lights a scattering of random bulbs, one arrangement per press.",
                    (frame, random) => WallPatterns.ApplyRandomSparkle(frame, random, SparkleCellCount))
            };
        }

        /// <summary>
        /// Creates the flipbook-style animations.
        ///
        /// The frames-per-second figures come from the timer intervals the old
        /// version used, converted into a rate. A 180-millisecond interval is
        /// about 5.6 frames a second, and so on. Using the equivalent rates
        /// keeps these animations looking exactly as they did before.
        /// </summary>
        private static IReadOnlyList<IWallEffect> BuildSequenceAnimations()
        {
            return new List<IWallEffect>
            {
                new FrameSequenceEffect(
                    "Row Sweep",
                    "A lit row travels down the wall and back up again.",
                    WallAnimations.CreateRowSweepFrames(),
                    framesPerSecond: 5.6),

                new FrameSequenceEffect(
                    "Border Pulse",
                    "The wall collapses from its outer edge to the centre and " +
                    "expands back out.",
                    WallAnimations.CreateBorderPulseFrames(),
                    framesPerSecond: 4.2),

                new FrameSequenceEffect(
                    "Spiral",
                    "Bulbs light in a spiral from the outside in, then unwind " +
                    "back out to darkness.",
                    WallAnimations.CreateSpiralInOutFrames(),
                    framesPerSecond: 8.3)
            };
        }

        /// <summary>
        /// Creates the calculated animations.
        ///
        /// Unlike the sequence animations above, these hold no frames at all.
        /// Each works out its picture from the current time whenever asked.
        /// </summary>
        private static IReadOnlyList<IWallEffect> BuildProceduralAnimations()
        {
            return new List<IWallEffect>
            {
                new MeteorEffect(),
                new SparkleStormEffect(),
                new EqBumperEffect(),
                new BeatFlashEffect(),
                new TempoPulseEffect(),
                new StarburstEffect(),
                new BreathingEffect(),
                new WiggleBreathingEffect(),
                new EqBreathingEffect()
            };
        }
    }
}
