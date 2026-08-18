using LightWall.Core.Models;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// The common shape that every visual behavior in the app must have.
    ///
    /// WHAT AN INTERFACE IS
    ///
    /// An interface is a contract. It says "anything calling itself an
    /// IWallEffect must provide these three things." It contains no actual code
    /// of its own - each effect supplies its own version.
    ///
    /// WHY WE WANT ONE HERE
    ///
    /// Before this existed, the project had three unrelated kinds of visual:
    ///
    ///   static patterns    void ApplyCheckerboard(WallFrame frame)
    ///   frame lists        List&lt;WallFrame&gt; CreateRowSweepFrames()
    ///   procedural         WallFrame GenerateMeteorFrame(int step, int tail)
    ///
    /// Three different shapes meant that anything wanting to work with "whatever
    /// is currently playing" needed a special case for each. That is manageable
    /// with nine effects. It becomes painful with thirty, and it becomes a
    /// genuine roadblock once audio needs to drive the choice of what plays.
    ///
    /// Now all three kinds present the same face to the rest of the app, so
    /// anything that can handle one can handle all of them:
    ///
    /// - the engine just calls Render and does not care what kind it is
    /// - the serial layer will just send whatever the engine produced
    /// - a future scene list can show DisplayName and Description in a menu
    /// - a future audio system can pick effects without knowing their internals
    ///
    /// The original WallPatterns, WallAnimations and WallProceduralAnimations
    /// classes still exist and still do the actual drawing. This interface sits
    /// on top of them and gives them a shared front door.
    /// </summary>
    public interface IWallEffect
    {
        /// <summary>
        /// The name a human sees, for example "Row Sweep".
        ///
        /// This exists so the user interface never has to hard-code a list of
        /// names. It can ask each effect what it is called. That matters for the
        /// eventual goal of a DJ picking effects from a list.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// A short plain-English description of what the effect looks like.
        ///
        /// Intended for tooltips and, later, for a scene-picker interface aimed
        /// at someone who did not write the code.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Draws this effect's current appearance into the supplied frame.
        ///
        /// The rules every effect follows:
        ///
        /// 1. Look at context.TimeSeconds to decide what "now" looks like.
        /// 2. Write the result into 'target'.
        /// 3. Take responsibility for the whole wall - clear anything from the
        ///    previous frame that should no longer be lit.
        ///
        /// Point 3 matters. The engine reuses the same WallFrame object every
        /// frame rather than creating a new one, because creating and throwing
        /// away objects sixty times a second is wasteful. The trade-off is that
        /// 'target' arrives still holding the previous frame's contents, so an
        /// effect that only turns cells on would leave old cells stuck lit.
        /// Nearly every effect should start with target.Clear().
        /// </summary>
        void Render(EffectContext context, WallFrame target);

        // ------------------------------------------------------------------
        // WHAT FOLLOWS, AND WHY IT LOOKS DIFFERENT FROM THE THREE ABOVE
        //
        // These two have a body: "=> false" and "=> EffectControl.None". An
        // interface with actual code in it looks wrong at first glance, since
        // the note at the top of this file says an interface contains no code
        // of its own.
        //
        // That was true of C# for a long time and stopped being true a few
        // versions ago. A member written this way is a DEFAULT: an effect that
        // says nothing gets this answer, and one that wants a different answer
        // writes its own.
        //
        // It is used here because the honest answer for most effects is "no"
        // and "none". Twenty-odd effects would otherwise each need a line
        // saying so, which is a lot of noise for no information - and the day
        // somebody adds an effect and forgets, the compiler would stop them for
        // a reason that does not matter.
        //
        // The trade worth knowing: a default is only visible through the
        // interface. Code holding a MeteorEffect directly cannot see
        // ReactsToAudio unless MeteorEffect declares it. Everything here works
        // through IWallEffect, so that never bites, but it is why this should
        // not become a habit for members that matter to the classes themselves.
        // ------------------------------------------------------------------

        /// <summary>
        /// Whether this effect does anything with the music.
        ///
        /// Used to decide which tab of the window an effect belongs in. An
        /// effect that reads nothing from EffectContext.Audio should leave this
        /// alone, and one that reads anything at all should say so.
        ///
        /// It is about whether audio is READ, not about whether it looks good
        /// with music playing. Meteor is perfectly nice over a track and still
        /// answers no, because nothing about what it draws depends on the sound.
        /// </summary>
        bool ReactsToAudio => false;

        /// <summary>
        /// Which of the effect-specific front-panel controls this one reads.
        ///
        /// The window shows only the controls the running effect has asked for,
        /// so a slider on screen always does something. See EffectControl.
        ///
        /// Only settings belonging to particular effects go here. Speed and the
        /// centre offsets are applied by the engine to whatever is playing, so
        /// they are always shown and are nobody's to declare.
        /// </summary>
        EffectControl Controls => EffectControl.None;
    }
}
