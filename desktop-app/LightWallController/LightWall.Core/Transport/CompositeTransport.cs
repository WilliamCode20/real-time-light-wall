using System;
using System.Collections.Generic;
using System.Linq;

namespace LightWall.Core.Transport
{
    /// <summary>
    /// Sends every packet to several transports at once.
    ///
    /// WHY THIS EXISTS
    ///
    /// So the virtual wall keeps running while the real wall is connected.
    ///
    /// That sounds redundant, but it is the most useful diagnostic the project
    /// has. When the physical wall does something unexpected, the immediate
    /// question is whether the app sent the wrong thing or the hardware did the
    /// wrong thing with it. Watching both walls side by side answers that in a
    /// glance:
    ///
    ///   both walls agree, hardware wrong  -> the problem is the wiring, the
    ///                                        firmware, or a relay
    ///   virtual wall already wrong        -> the problem is upstream, in the
    ///                                        app, and no cable is involved
    ///
    /// Without this the virtual wall would go dark the moment serial was
    /// attached, exactly when it becomes most valuable.
    ///
    /// It costs almost nothing. The virtual wall is a few dozen bytes of
    /// arithmetic per packet, thirty times a second.
    /// </summary>
    public sealed class CompositeTransport : IWallTransport
    {
        /// <summary>
        /// The transports being fed, in the order they were supplied.
        /// </summary>
        private readonly IWallTransport[] _transports;

        /// <summary>
        /// Creates a transport that forwards to all of the given transports.
        /// </summary>
        public CompositeTransport(params IWallTransport[] transports)
        {
            if (transports is null || transports.Length == 0)
            {
                throw new ArgumentException(
                    "At least one transport is required.",
                    nameof(transports));
            }

            if (transports.Any(t => t is null))
            {
                throw new ArgumentException(
                    "None of the transports may be null.",
                    nameof(transports));
            }

            _transports = transports;
        }

        /// <summary>
        /// The transports being fed, for anything that needs to inspect them
        /// individually - the interface reading the virtual wall's statistics,
        /// for instance.
        /// </summary>
        public IReadOnlyList<IWallTransport> Transports => _transports;

        /// <inheritdoc />
        public string Name => string.Join(" + ", _transports.Select(t => t.Name));

        /// <summary>
        /// True when at least one transport is connected.
        ///
        /// Deliberately "at least one" rather than "all". If the cable is
        /// unplugged but the virtual wall is still running, output should keep
        /// flowing - that way the app carries on working, the virtual wall keeps
        /// showing what the wall would be doing, and everything resumes by
        /// itself when the cable comes back.
        /// </summary>
        public bool IsConnected => _transports.Any(t => t.IsConnected);

        /// <inheritdoc />
        public void Connect()
        {
            foreach (IWallTransport transport in _transports)
            {
                transport.Connect();
            }
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            foreach (IWallTransport transport in _transports)
            {
                try
                {
                    transport.Disconnect();
                }
                catch (Exception)
                {
                    // One transport failing to close must not prevent the others
                    // from being closed. This runs during shutdown, where the
                    // priority is releasing everything rather than reporting.
                }
            }
        }

        /// <summary>
        /// Sends the packet to every transport.
        ///
        /// One failing transport must not stop the others receiving the packet.
        /// If the USB cable is pulled, the virtual wall should carry on
        /// perfectly happily — so every transport is attempted, and only then is
        /// the first failure reported onward.
        ///
        /// Reporting it at all matters: the output service uses the exception to
        /// know something went wrong, and swallowing it entirely would hide a
        /// dead cable completely.
        /// </summary>
        public void Send(byte[] packet)
        {
            if (packet is null)
            {
                throw new ArgumentNullException(nameof(packet));
            }

            Exception? firstFailure = null;

            foreach (IWallTransport transport in _transports)
            {
                try
                {
                    transport.Send(packet);
                }
                catch (Exception ex)
                {
                    firstFailure ??= ex;
                }
            }

            if (firstFailure is not null)
            {
                throw firstFailure;
            }
        }

        /// <summary>
        /// Disposes every transport.
        /// </summary>
        public void Dispose()
        {
            foreach (IWallTransport transport in _transports)
            {
                try
                {
                    transport.Dispose();
                }
                catch (Exception)
                {
                    // As with Disconnect: release everything, report nothing.
                }
            }
        }
    }
}
