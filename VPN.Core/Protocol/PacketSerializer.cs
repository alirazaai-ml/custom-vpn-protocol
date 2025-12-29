using System;
using System.IO;
using System.Text;
using System.Text.Json;
using VPN.Core.Enums;
using VPN.Core.Exceptions;
using VPN.Core.Models;

namespace VPN.Core.Protocol
{
    /// <summary>
    /// Serializes and deserializes VPN packets to/from binary format
    /// </summary>
    public static class PacketSerializer
    {
        /// <summary>
        /// Serialize VpnPacket to byte array
        /// </summary>
        public static byte[] Serialize(VpnPacket packet)
        {
            try
            {
                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms, Encoding.UTF8);

                // Write header
                writer.Write(ProtocolConstants.MAGIC_HEADER);      // 4 bytes magic
                writer.Write(packet.Version);                     // 1 byte version
                writer.Write((byte)packet.Type);                  // 1 byte type
                writer.Write(packet.SessionId);                   // 4 bytes session ID
                writer.Write(packet.SequenceNumber);              // 4 bytes sequence
                writer.Write(packet.PayloadLength);               // 2 bytes payload length

                // Write payload if exists
                if (packet.Payload != null && packet.Payload.Length > 0)
                {
                    writer.Write(packet.Payload);
                }

                // Write HMAC if exists (for data packets)
                if (packet.Hmac != null && packet.Hmac.Length > 0)
                {
                    writer.Write(packet.Hmac);
                }

                return ms.ToArray();
            }
            catch (Exception ex)
            {
                throw new VpnException("Packet serialization failed", ex);
            }
        }

        /// <summary>
        /// Deserialize byte array to VpnPacket
        /// </summary>
        public static VpnPacket Deserialize(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms, Encoding.UTF8);

                // Read and validate magic header
                byte[] magic = reader.ReadBytes(4);
                if (!CompareBytes(magic, ProtocolConstants.MAGIC_HEADER))
                    throw new VpnException("Invalid packet: Magic header mismatch");

                var packet = new VpnPacket
                {
                    Version = reader.ReadByte(),
                    Type = (PacketType)reader.ReadByte(),
                    SessionId = reader.ReadInt32(),
                    SequenceNumber = reader.ReadInt32(),
                    PayloadLength = reader.ReadUInt16()
                };

                // Read payload
                if (packet.PayloadLength > 0)
                {
                    packet.Payload = reader.ReadBytes(packet.PayloadLength);
                }
                else
                {
                    packet.Payload = Array.Empty<byte>();
                }

                // Read HMAC if packet is data packet
                if (packet.IsDataPacket && reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    int remainingBytes = (int)(reader.BaseStream.Length - reader.BaseStream.Position);
                    if (remainingBytes >= ProtocolConstants.HMAC_SIZE)
                    {
                        packet.Hmac = reader.ReadBytes(ProtocolConstants.HMAC_SIZE);
                    }
                }

                return packet;
            }
            catch (Exception ex)
            {
                throw new VpnException("Packet deserialization failed", ex);
            }
        }

        /// <summary>
        /// Serialize handshake request to JSON bytes
        /// </summary>
        public static byte[] SerializeHandshakeRequest(HandshakeRequest request)
        {
            try
            {
                string json = JsonSerializer.Serialize(request);
                return Encoding.UTF8.GetBytes(json);
            }
            catch (Exception ex)
            {
                throw new VpnException("Handshake request serialization failed", ex);
            }
        }

        /// <summary>
        /// Deserialize handshake response from JSON bytes
        /// </summary>
        public static HandshakeResponse DeserializeHandshakeResponse(byte[] data)
        {
            try
            {
                string json = Encoding.UTF8.GetString(data);
                return JsonSerializer.Deserialize<HandshakeResponse>(json)
                    ?? throw new VpnException("Failed to deserialize handshake response");
            }
            catch (Exception ex)
            {
                throw new VpnException("Handshake response deserialization failed", ex);
            }
        }

        private static bool CompareBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        /// <summary>
        /// Calculate total packet size
        /// </summary>
        public static int CalculatePacketSize(VpnPacket packet)
        {
            int size = ProtocolConstants.HEADER_SIZE;                    // Base header
            size += packet.Payload?.Length ?? 0;                         // Payload
            size += packet.IsDataPacket ? ProtocolConstants.HMAC_SIZE : 0; // HMAC if data packet
            return size;
        }
    }
}