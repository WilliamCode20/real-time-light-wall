using System;

namespace LightWall.Core.Transport
{
    /// <summary>
    /// Something that can carry packets to a wall.
    ///
    /// WHY THIS IS AN INTERFACE
    ///
    /// There will be at least two of these, and the app should not care which
    /// one it is using:
    ///
    ///   LoopbackTransport - feeds the bytes to a software model of the Arduino
    ///                       and shows what the wall would do. No hardware, no
    ///                       hot warehouse, and it can be made to drop bytes on
    ///                       purpose.
    ///
    ///   SerialTransport   - the real thing, over a USB cable. Not yet written.
    ///
    /// Because both present the same face, everything upstream - the output
    /// service, the rate limiting, the packet building - is written once and
    /// works with either. When the real serial version arrives it slots in
    /// without changing anything above it, which also means every hour spent
    /// testing against the loopback is testing the real code path too.
    ///
    /// A third implementation may well be worth having later: one that records
    /// packets to a file for offline inspection.
    /// </summary>
    public interface IWallTransport : IDisposable
    {
        /// <summary>
        /// A short human-readable name, such as "Loopback" or "COM3".
        /// Shown in the interface so the user knows what they are driving.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// True when the transport is open and able to accept packets.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Opens the connection.
        ///
        /// For the real serial version this is where the port is opened - and
        /// where a subtlety lives that is worth knowing about in advance:
        /// opening a serial port to an Arduino Mega toggles the DTR line, which
        /// RESETS the board. For roughly the first one and a half to two seconds
        /// afterwards the bootloader is running and will ignore anything sent.
        /// That implementation will need to wait before it starts talking.
        /// </summary>
        void Connect();

        /// <summary>
        /// Closes the connection.
        ///
        /// Implementations should try to leave the wall dark rather than frozen
        /// on whatever frame happened to be showing.
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Sends one packet.
        ///
        /// Called from the output service's background thread, never from the
        /// interface thread, so an implementation is allowed to block here.
        /// </summary>
        void Send(byte[] packet);
    }
}
