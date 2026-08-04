namespace LightWall.Core.Serialization
{
    /// <summary>
    /// The kinds of instruction the desktop app can send to the Arduino.
    ///
    /// This value occupies byte 2 of every packet. Giving each instruction a
    /// name here rather than scattering raw numbers like 0x01 through the code
    /// means the intent is readable at a glance, and the compiler catches typos.
    ///
    /// THESE NUMBERS ARE A CONTRACT
    ///
    /// The Arduino firmware will have its own matching list of these values.
    /// Once hardware is running, existing numbers must never be reassigned - a
    /// wall running older firmware would happily misinterpret them. New
    /// instructions get new numbers; old ones keep theirs forever.
    ///
    /// The underlying type is byte because a packet field is one byte wide.
    /// </summary>
    public enum PacketCommand : byte
    {
        /// <summary>
        /// Show this wall state. The payload holds the 35 bulb bits.
        /// This is the everyday packet, sent continuously while running.
        /// </summary>
        FrameUpdate = 0x01,

        /// <summary>
        /// Switch every bulb off. The payload is ignored.
        ///
        /// Kept separate from "a frame that happens to be all zeros" so the
        /// firmware has an unambiguous instruction to go dark - including as its
        /// own safety response when it stops hearing from the app.
        /// </summary>
        Blackout = 0x02,

        /// <summary>
        /// "Still here." The payload is ignored.
        ///
        /// Intended for the planned firmware watchdog: if nothing arrives for a
        /// set period the wall blanks itself, so that a crashed app or an
        /// unplugged cable leaves the wall dark rather than stuck on whatever
        /// frame it happened to be showing.
        /// </summary>
        Heartbeat = 0x03
    }
}
