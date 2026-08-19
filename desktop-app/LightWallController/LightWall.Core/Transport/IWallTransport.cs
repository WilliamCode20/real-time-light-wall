using System;

namespace LightWall.Core.Transport
{
    /// <summary>
    /// Something that can carry packets to a wall.
    ///
    /// WHY THIS IS AN INTERFACE
    ///
    /// There are three of these, and the app cannot tell them apart:
    ///
    ///   LoopbackTransport  - feeds the bytes to a software model of the Arduino
    ///                        and shows what the wall would do. No hardware, no
    ///                        hot warehouse, and it can be made to drop bytes on
    ///                        purpose.
    ///
    ///   SerialTransport    - the real thing, over a USB cable.
    ///
    ///   CompositeTransport - both at once, which is how the app actually runs
    ///                        when a port is connected. The virtual wall stays
    ///                        beside the real one rather than being replaced.
    ///
    /// Because they all present the same face, everything upstream - the output
    /// service, the rate limiting, the packet building - is written once and
    /// works with any of them. That paid off exactly as intended: when the real
    /// serial version arrived it slotted in without a line changing above it,
    /// which also means every hour spent testing against the loopback had been
    /// testing the real code path all along.
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
        /// For the serial version this is where the port is opened - and where a
        /// subtlety lives that catches everybody once: opening a serial port to
        /// an Arduino Mega toggles the DTR line, which RESETS the board. For
        /// roughly the first one and a half to two seconds afterwards the
        /// bootloader is running and ignores anything sent.
        ///
        /// SerialTransport handles that by discarding packets during a settle
        /// window rather than blocking here, since Connect is called from the
        /// window and blocking would freeze it.
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
