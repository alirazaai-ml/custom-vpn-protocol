using System;
using VPN.Core.Enums;
using VPN.Core.Models;

namespace VPN.Core.Protocol
{
    /// <summary>
    /// Helper class for building VPN packets
    /// </summary>
    public static class PacketBuilder
    {
        private static int _nextSequenceNumber = 1;

        /// <summary>
        /// Create a handshake request packet
        /// </summary>
        public static VpnPacket CreateHandshakeRequest(HandshakeRequest request, int sessionId = 0)
        {
            byte[] payload = PacketSerializer.SerializeHandshakeRequest(request);

            return new VpnPacket
            {
                Version = ProtocolConstants.PROTOCOL_VERSION,
                Type = PacketType.HandshakeRequest,
                SessionId = sessionId,
                SequenceNumber = GetNextSequenceNumber(),
                Payload = payload,
                PayloadLength = (ushort)payload.Length
            };
        }

        /// <summary>
        /// Create a handshake response packet
        /// </summary>
        public static VpnPacket CreateHandshakeResponse(HandshakeResponse response, int sessionId)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(response);
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(json);

            return new VpnPacket
            {
                Version = ProtocolConstants.PROTOCOL_VERSION,
                Type = PacketType.HandshakeResponse,
                SessionId = sessionId,
                SequenceNumber = GetNextSequenceNumber(),
                Payload = payload,
                PayloadLength = (ushort)payload.Length
            };
        }

        /// <summary>
        /// Create a data packet with encrypted payload
        /// </summary>
        public static VpnPacket CreateDataPacket(int sessionId, byte[] encryptedData)
        {
            return new VpnPacket
            {
                Version = ProtocolConstants.PROTOCOL_VERSION,
                Type = PacketType.Data,
                SessionId = sessionId,
                SequenceNumber = GetNextSequenceNumber(),
                Payload = encryptedData,
                PayloadLength = (ushort)encryptedData.Length
            };
        }

        /// <summary>
        /// Create a keep-alive packet
        /// </summary>
        public static VpnPacket CreateKeepAlivePacket(int sessionId)
        {
            return new VpnPacket
            {
                Version = ProtocolConstants.PROTOCOL_VERSION,
                Type = PacketType.KeepAlive,
                SessionId = sessionId,
                SequenceNumber = GetNextSequenceNumber(),
                Payload = Array.Empty<byte>(),
                PayloadLength = 0
            };
        }

        /// <summary>
        /// Create a disconnect packet
        /// </summary>
        public static VpnPacket CreateDisconnectPacket(int sessionId, string reason = "")
        {
            byte[] payload = string.IsNullOrEmpty(reason)
                ? Array.Empty<byte>()
                : System.Text.Encoding.UTF8.GetBytes(reason);

            return new VpnPacket
            {
                Version = ProtocolConstants.PROTOCOL_VERSION,
                Type = PacketType.Disconnect,
                SessionId = sessionId,
                SequenceNumber = GetNextSequenceNumber(),
                Payload = payload,
                PayloadLength = (ushort)payload.Length
            };
        }

        /// <summary>
        /// Create an error packet
        /// </summary>
        public static VpnPacket CreateErrorPacket(int sessionId, int errorCode, string message)
        {
            var errorData = new
            {
                code = errorCode,
                message = message,
                timestamp = DateTime.UtcNow
            };

            string json = System.Text.Json.JsonSerializer.Serialize(errorData);
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(json);

            return new VpnPacket
            {
                Version = ProtocolConstants.PROTOCOL_VERSION,
                Type = PacketType.Error,
                SessionId = sessionId,
                SequenceNumber = GetNextSequenceNumber(),
                Payload = payload,
                PayloadLength = (ushort)payload.Length
            };
        }

        private static int GetNextSequenceNumber()
        {
            return _nextSequenceNumber++;
        }

        /// <summary>
        /// Reset sequence number (for testing)
        /// </summary>
        public static void ResetSequenceNumber()
        {
            _nextSequenceNumber = 1;
        }

        public static VpnPacket CreateErrorPacket(object value, int errorCode, string message)
        {
            throw new NotImplementedException();
        }
    }
}