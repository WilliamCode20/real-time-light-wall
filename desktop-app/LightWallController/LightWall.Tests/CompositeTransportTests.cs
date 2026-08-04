using System;
using System.Collections.Generic;
using LightWall.Core.Models;
using LightWall.Core.Serialization;
using LightWall.Core.Transport;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the transport that feeds several walls at once.
    ///
    /// The behaviour that matters most here is what happens when one transport
    /// fails. Once the real wall is connected, the USB cable is the least
    /// reliable part of the whole system — and a pulled cable must not take the
    /// virtual wall down with it, because the virtual wall is exactly what you
    /// want to be looking at while working out what went wrong.
    /// </summary>
    public class CompositeTransportTests
    {
        /// <summary>Records what it was sent.</summary>
        private sealed class RecordingTransport : IWallTransport
        {
            public List<byte[]> Packets { get; } = new();

            public string Name { get; init; } = "Recording";

            public bool IsConnected { get; private set; }

            public void Connect() => IsConnected = true;

            public void Disconnect() => IsConnected = false;

            public void Send(byte[] packet) => Packets.Add((byte[])packet.Clone());

            public void Dispose() => Disconnect();
        }

        /// <summary>Stands in for an unplugged cable.</summary>
        private sealed class FailingTransport : IWallTransport
        {
            public string Name => "Failing";

            public bool IsConnected { get; private set; }

            public int SendAttempts { get; private set; }

            public void Connect() => IsConnected = true;

            public void Disconnect() => IsConnected = false;

            public void Send(byte[] packet)
            {
                SendAttempts++;
                throw new InvalidOperationException("cable unplugged");
            }

            public void Dispose() => Disconnect();
        }

        private static byte[] SamplePacket()
        {
            var frame = new WallFrame();
            frame.SetCell(2, 3, true);
            return WallFrameSerializer.CreateFramePacket(frame);
        }

        [Fact]
        public void RequiresAtLeastOneTransport()
        {
            Assert.Throws<ArgumentException>(() => new CompositeTransport());
            Assert.Throws<ArgumentException>(() => new CompositeTransport(null!));
        }

        [Fact]
        public void SendsToEveryTransport()
        {
            var first = new RecordingTransport();
            var second = new RecordingTransport();

            using var composite = new CompositeTransport(first, second);
            composite.Connect();
            composite.Send(SamplePacket());

            Assert.Single(first.Packets);
            Assert.Single(second.Packets);
            Assert.Equal(first.Packets[0], second.Packets[0]);
        }

        [Fact]
        public void ConnectsAndDisconnectsEverything()
        {
            var first = new RecordingTransport();
            var second = new RecordingTransport();

            using var composite = new CompositeTransport(first, second);

            composite.Connect();
            Assert.True(first.IsConnected);
            Assert.True(second.IsConnected);

            composite.Disconnect();
            Assert.False(first.IsConnected);
            Assert.False(second.IsConnected);
        }

        /// <summary>
        /// The important one. A dead cable must not stop the virtual wall.
        /// </summary>
        [Fact]
        public void OneFailingTransport_DoesNotStopTheOthersReceiving()
        {
            var working = new RecordingTransport();
            var broken = new FailingTransport();

            // Broken one first, so a naive implementation that gave up on the
            // first exception would never reach the working one.
            using var composite = new CompositeTransport(broken, working);
            composite.Connect();

            Assert.Throws<InvalidOperationException>(() => composite.Send(SamplePacket()));

            // The failure was reported, but the working transport still got the
            // packet.
            Assert.Single(working.Packets);
            Assert.Equal(1, broken.SendAttempts);
        }

        [Fact]
        public void AFailureIsStillReported()
        {
            // The output service needs to hear about a dead cable. Swallowing
            // the exception entirely would hide it completely.
            var working = new RecordingTransport();
            var broken = new FailingTransport();

            using var composite = new CompositeTransport(working, broken);
            composite.Connect();

            Assert.Throws<InvalidOperationException>(() => composite.Send(SamplePacket()));
        }

        [Fact]
        public void IsConnectedWhenAnyTransportIs()
        {
            // "Any" rather than "all" on purpose. If the cable is unplugged but
            // the virtual wall is still running, output should keep flowing so
            // everything resumes by itself when the cable comes back.
            var first = new RecordingTransport();
            var second = new RecordingTransport();

            using var composite = new CompositeTransport(first, second);

            Assert.False(composite.IsConnected);

            first.Connect();
            Assert.True(composite.IsConnected);

            first.Disconnect();
            Assert.False(composite.IsConnected);
        }

        [Fact]
        public void NameListsEveryTransport()
        {
            using var composite = new CompositeTransport(
                new RecordingTransport { Name = "Virtual" },
                new RecordingTransport { Name = "COM3" });

            Assert.Contains("Virtual", composite.Name);
            Assert.Contains("COM3", composite.Name);
        }

        [Fact]
        public void SendingNothingIsRejected()
        {
            using var composite = new CompositeTransport(new RecordingTransport());

            Assert.Throws<ArgumentNullException>(() => composite.Send(null!));
        }

        /// <summary>
        /// The real pairing: the virtual wall alongside something else.
        /// </summary>
        [Fact]
        public void TheVirtualWallStillDecodesWhilePairedWithAnother()
        {
            var loopback = new LoopbackTransport();
            var other = new RecordingTransport();

            using var composite = new CompositeTransport(loopback, other);
            composite.Connect();

            var sent = new WallFrame();
            sent.SetCell(1, 1, true);
            sent.SetCell(4, 6, true);

            composite.Send(WallFrameSerializer.CreateFramePacket(sent));

            var received = new WallFrame();
            loopback.CopyReceivedFrameTo(received);

            Assert.True(received.ContentEquals(sent));
            Assert.Single(other.Packets);
        }
    }
}
