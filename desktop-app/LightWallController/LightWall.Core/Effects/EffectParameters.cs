using System;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Holds the user-adjustable settings that effects can read while drawing.
    ///
    /// Think of this as "the knobs on the front panel".
    ///
    /// The user moves a slider in the window, the window writes the new value
    /// into this object, and the next time an effect draws a frame it reads the
    /// updated value. That means sliders take effect immediately, mid-animation,
    /// without restarting anything.
    ///
    /// Note on what is NOT in here:
    ///
    /// Speed and the Center X/Y offsets are deliberately absent. Those are not
    /// handled by individual effects - they are handled by WallEngine, because
    /// they apply the same way to every effect. An effect should only need to
    /// worry about settings that are genuinely its own business.
    ///
    /// Note on future growth:
    ///
    /// Right now there is exactly one effect-specific setting, so a single
    /// shared object is the simplest thing that works. Once there are many
    /// effects each with their own settings, this will want to become a
    /// per-effect parameter system instead. That change is easy to make later
    /// and there is no benefit to building it before it is needed.
    /// </summary>
    public sealed class EffectParameters
    {
        /// <summary>
        /// How many cells long the meteor's glowing trail should be.
        ///
        /// Used only by MeteorEffect. A value of 1 means "just the head, no tail".
        /// </summary>
        public int MeteorTailLength { get; set; } = 3;

        /// <summary>
        /// Which single bulb the Identify Bulb effect should light, from 0 to 34.
        ///
        /// Used only during hardware checking, to confirm that the relay labels
        /// and the pin map match the physical wall.
        /// </summary>
        public int IdentifyBulbIndex { get; set; }

        /// <summary>
        /// Creates a copy of these parameters.
        ///
        /// This is useful when something needs a stable snapshot of the settings
        /// that will not change underneath it - for example a test, or (later) a
        /// background thread that renders frames while the user is still moving
        /// sliders on the main thread.
        /// </summary>
        public EffectParameters Clone()
        {
            return new EffectParameters
            {
                MeteorTailLength = MeteorTailLength,
                IdentifyBulbIndex = IdentifyBulbIndex
            };
        }
    }
}
