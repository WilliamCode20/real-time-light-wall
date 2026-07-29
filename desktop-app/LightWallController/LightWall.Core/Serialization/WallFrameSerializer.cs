using System;
using System.Collections.Generic;
using System.Text;
using LightWall.Core.Models;

namespace LightWall.Core.Serialization
{
    /// <summary>
    /// Converts WallFrame objects into compact byte-based data packets.
    ///
    /// This class is the bridge between:
    /// - the app's wall-state model
    /// - future serial communication to the Arduino
    ///
    /// Design choice:
    /// The 5x7 wall has 35 cells. Since each cell is only ON or OFF, each cell
    /// only needs 1 bit. That means the whole wall can be packed into 5 bytes.
    ///
    /// Bit mapping uses row-major order:
    /// - row 0, col 0 -> bit 0
    /// - row 0, col 1 -> bit 1
    /// - ...
    /// - row 4, col 6 -> bit 34
    ///
    /// Packet format:
    /// Byte 0 = start byte
    /// Byte 1 = command byte
    /// Byte 2-6 = packed wall payload (5 bytes)
    /// Byte 7 = checksum
    /// </summary>
    public static class WallFrameSerializer
    {
        /// <summary>
        /// Marks the start of a packet.
        /// The Arduino will use this to find packet boundaries.
        /// </summary>
        public const byte StartByte = 0xAA;

        /// <summary>
        /// Command byte for a "frame update" packet.
        /// This gives us room for future command types later.
        /// </summary>
        public const byte FrameCommand = 0x01;

        /// <summary>
        /// Number of payload bytes needed to store 35 bits of wall state.
        /// 35 bits fits inside 5 bytes (40 bits total).
        /// </summary>
        public const int PayloadLength = 5;

        /// <summary>
        /// Total packet size:
        /// start byte + command byte + 5 payload bytes + checksum byte
        /// </summary>
        public const int PacketLength = 8;

        /// <summary>
        /// Packs a WallFrame into a 5-byte payload.
        ///
        /// Example:
        /// If cell (0,0) is ON, bit 0 of payload[0] becomes 1.
        /// If cell (0,1) is ON, bit 1 of payload[0] becomes 1.
        ///
        /// Bits 35-39 are unused and remain 0.
        /// </summary>
        public static byte[] SerializeFrameData(WallFrame frame)
        {
            var payload = new byte[PayloadLength];

            for (int row = 0; row < WallFrame.Rows; row++)
            {
                for (int column = 0; column < WallFrame.Columns; column++)
                {
                    if (!frame.GetCell(row, column))
                    {
                        continue;
                    }

                    int bitIndex = GetBitIndex(row, column);
                    int byteIndex = bitIndex / 8;
                    int bitOffset = bitIndex % 8;

                    payload[byteIndex] |= (byte)(1 << bitOffset);
                }
            }

            return payload;
        }

        /// <summary>
        /// Builds the full 8-byte packet:
        /// [Start][Command][Payload x 5][Checksum]
        /// </summary>
        public static byte[] CreateFramePacket(WallFrame frame)
        {
            byte[] payload = SerializeFrameData(frame);
            byte checksum = CalculateChecksum(FrameCommand, payload);

            return new byte[]
            {
                StartByte,
                FrameCommand,
                payload[0],
                payload[1],
                payload[2],
                payload[3],
                payload[4],
                checksum
            };
        }

        /// <summary>
        /// Converts a byte array into a readable hex string for debugging.
        ///
        /// Example output:
        /// AA 01 7F 00 00 00 00 7E
        ///
        /// This is very useful while validating packet structure before
        /// real serial communication is added.
        /// </summary>
        public static string ToHexString(byte[] bytes)
        {
            return string.Join(" ", bytes.Select(b => b.ToString("X2")));
        }

        /// <summary>
        /// Computes the row-major bit index for one wall cell.
        ///
        /// This matches the logical 5x7 ordering:
        /// row 0 -> first 7 bits
        /// row 1 -> next 7 bits
        /// etc.
        /// </summary>
        private static int GetBitIndex(int row, int column)
        {
            return (row * WallFrame.Columns) + column;
        }

        /// <summary>
        /// Computes a simple checksum by XOR-ing the command byte and payload bytes.
        ///
        /// This is not cryptographic security. It is only a lightweight way to
        /// detect corrupted or misaligned packets.
        /// </summary>
        private static byte CalculateChecksum(byte command, byte[] payload)
        {
            byte checksum = command;

            foreach (byte b in payload)
            {
                checksum ^= b;
            }

            return checksum;
        }
    }
}