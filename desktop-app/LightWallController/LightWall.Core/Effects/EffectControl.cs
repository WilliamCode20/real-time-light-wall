using System;

namespace LightWall.Core.Effects
{
    /// <summary>
    /// Which of the front-panel controls an effect actually pays attention to.
    ///
    /// WHY AN EFFECT DECLARES THIS RATHER THAN THE WINDOW KNOWING
    ///
    /// Most controls apply to everything - speed and the centre offsets are
    /// handled by the engine and affect whatever is playing. A few do not. The
    /// meteor tail length means nothing unless Meteor is running, and the fill
    /// pacing means nothing unless one of the Fill and Clear effects is.
    ///
    /// Showing all of them all of the time makes the panel longer than it needs
    /// to be and invites the reasonable-but-wrong conclusion that dragging a
    /// slider ought to change something. So the window shows only the controls
    /// the running effect has asked for.
    ///
    /// It would have been easy to write that rule into the window as a list of
    /// effect names. That is exactly the arrangement the catalogue exists to
    /// avoid: adding an effect would mean editing the window as well, and the
    /// two lists would drift apart the first time somebody forgot. An effect
    /// knows which settings it reads, so it is the honest place to say so.
    ///
    /// WHAT THE Flags ATTRIBUTE IS FOR
    ///
    /// An effect may want more than one of these - Fill and Clear wants both the
    /// beat source and the pacing. Marking the enum with Flags means the values
    /// can be combined with a single vertical bar, and tested with HasFlag:
    ///
    ///     Controls =&gt; EffectControl.BeatSource | EffectControl.FillPacing;
    ///
    /// For that to work each value has to occupy its own bit, which is why they
    /// go 1, 2, 4, 8 rather than 1, 2, 3, 4. The next one added must be 16.
    /// </summary>
    [Flags]
    public enum EffectControl
    {
        /// <summary>Nothing beyond the controls every effect shares.</summary>
        None = 0,

        /// <summary>How long the Meteor's trail is.</summary>
        MeteorTail = 1,

        /// <summary>
        /// Whether beats come from what was heard or from the tempo metronome.
        ///
        /// Note that Beat Flash and Tempo Pulse do NOT ask for this even though
        /// they are beat-driven, because each is deliberately pinned to one
        /// source. Not offering the control is more honest than offering one
        /// that would be ignored.
        /// </summary>
        BeatSource = 2,

        /// <summary>
        /// Whether a beat advances one picture or launches a whole sweep.
        /// </summary>
        FillPacing = 4,

        /// <summary>Which single bulb the hardware check is lighting.</summary>
        IdentifyBulb = 8
    }
}
