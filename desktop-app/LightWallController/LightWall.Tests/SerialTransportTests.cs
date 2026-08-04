using System;
using System.Linq;
using LightWall.Core.Serialization;
using LightWall.IO.Serial;

namespace LightWall.Tests
{
    /// <summary>
    /// Tests for the real serial transport.
    ///
    /// WHAT CAN AND CANNOT BE TESTED HERE
    ///
    /// These deliberately never open a real port. A test that needs an Arduino
    /// plugged in is a test that fails on every machine that does not have one,
    /// which means it gets ignored and then deleted.
    ///
    /// So this file covers the parts that are genuine logic - argument checking,
    /// state before connecting, behaviour when disconnected, port name sorting -
    /// and leaves the cable itself to the hardware bring-up session.
    ///
    /// That is a smaller share of the risk than it sounds, because the protocol,
    /// the framing, the recovery and the rate limiting are already proven
    /// against the virtual wall. What is genuinely untested until hardware
    /// arrives is the cable, the reset timing, and the wiring.
    /// </summary>
    public class SerialTransportTests
    {
        [Fact]
        public void Constructor_RejectsAMissingPortName()
        {
            Assert.Throws<ArgumentException>(() => new SerialTransport(""));
            Assert.Throws<ArgumentException>(() => new SerialTransport("   "));
        }

        [Fact]
        public void Name_DescribesThePortAndSpeed()
        {
            using var transport = new SerialTransport("COM3");

            Assert.Contains("COM3", transport.Name);
            Assert.Contains("115200", transport.Name);
        }

        [Fact]
        public void DefaultsAreTheOnesWeIntended()
        {
            using var transport = new SerialTransport("COM3");

            Assert.Equal(115200, transport.BaudRate);

            // The settle delay is what stops packets being fired at a board that
            // is still running its bootloader after the port opened and reset it.
            Assert.Equal(2.0, transport.ResetSettleSeconds);
        }

        [Fact]
        public void StartsDisconnected()
        {
            using var transport = new SerialTransport("COM3");

            Assert.False(transport.IsConnected);
            Assert.False(transport.IsWaitingForBoardReset);
            Assert.Null(transport.LastError);
        }

        [Fact]
        public void SendingWhileDisconnected_IsIgnoredRatherThanThrowing()
        {
            // The output service sends thirty times a second regardless. A
            // disconnected transport must absorb that quietly rather than
            // raising an exception on every attempt.
            using var transport = new SerialTransport("COM3");

            transport.Send(WallFrameSerializer.CreateFramePacket(new Core.Models.WallFrame()));

            Assert.Equal(0, transport.PacketsWritten);
        }

        [Fact]
        public void SendingNothing_IsRejected()
        {
            using var transport = new SerialTransport("COM3");

            Assert.Throws<ArgumentNullException>(() => transport.Send(null!));
        }

        [Fact]
        public void ConnectingToAPortThatDoesNotExist_FailsAndSaysWhy()
        {
            // A name no real machine will have.
            using var transport = new SerialTransport("COM_NOT_A_REAL_PORT");

            Assert.ThrowsAny<Exception>(() => transport.Connect());

            Assert.False(transport.IsConnected);
            Assert.NotNull(transport.LastError);
            Assert.Contains("COM_NOT_A_REAL_PORT", transport.LastError!);
        }

        [Fact]
        public void DisconnectingWithoutHavingConnected_IsHarmless()
        {
            // Detach is called on shutdown regardless of whether anything was
            // ever attached, so this path has to be safe.
            var transport = new SerialTransport("COM3");

            transport.Disconnect();
            transport.Disconnect();
            transport.Dispose();

            Assert.False(transport.IsConnected);
        }

        [Fact]
        public void PortLister_ReturnsWithoutThrowing()
        {
            // Whatever this machine has, listing ports must not fail. Zero ports
            // is a perfectly normal answer.
            string[] ports = SerialPortLister.GetAvailablePortNames();

            Assert.NotNull(ports);
        }

        [Fact]
        public void PortLister_SortsComPortsByNumberNotAsText()
        {
            // Sorted as plain text, "COM10" would come before "COM9", which
            // looks simply wrong in a dropdown.
            //
            // This machine may have no ports at all, so the assertion only
            // applies to whatever is actually present.
            string[] ports = SerialPortLister.GetAvailablePortNames();

            int[] numbers = ports
                .Where(p => p.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                .Select(p => int.TryParse(p.AsSpan(3), out int n) ? n : int.MaxValue)
                .Where(n => n != int.MaxValue)
                .ToArray();

            for (int i = 1; i < numbers.Length; i++)
            {
                Assert.True(
                    numbers[i] >= numbers[i - 1],
                    $"Ports came back out of order: COM{numbers[i - 1]} before COM{numbers[i]}.");
            }
        }

        [Fact]
        public void PortLister_HasNoDuplicates()
        {
            string[] ports = SerialPortLister.GetAvailablePortNames();

            Assert.Equal(ports.Length, ports.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }
}
