using System;
using System.Collections.Generic;
using System.Text;
using VPN.Core.Enums;

namespace VPN.Core.Models
{
    /// <summary>
    /// Main packet structure for VPN communication
    /// </summary>
    public class VpnPacket
    {
        // Header fields
        public byte Version { get; set; } = 0x01;      // Protocol version (1)
        public PacketType Type { get; set; }           // Packet type
        public int SessionId { get; set; }             // Unique session identifier
        public int SequenceNumber { get; set; }        // Packet sequence for ordering
        public ushort PayloadLength { get; set; }      // Length of encrypted payload

        // Data
        public byte[] Payload { get; set; } = Array.Empty<byte>();  // Encrypted data
        public byte[] Hmac { get; set; } = Array.Empty<byte>();     // Integrity check

        // Timestamp for tracking
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Calculated properties
        public bool IsControlPacket => (byte)Type < 0x10;
        public bool IsDataPacket => (byte)Type >= 0x10 && (byte)Type < 0xF0;
        public bool IsErrorPacket => Type == PacketType.Error;
    }
}
