using System;
using System.Collections.Generic;
using System.Text;

using VPN.Core.Models;

namespace VPN.Core.Interfaces
{
    /// <summary>
    /// Interface for packet serialization
    /// </summary>
    public interface IPacketSerializer
    {
        /// <summary>
        /// Serialize packet to bytes
        /// </summary>
        byte[] Serialize(VpnPacket packet);

        /// <summary>
        /// Deserialize bytes to packet
        /// </summary>
        VpnPacket Deserialize(byte[] data);

        /// <summary>
        /// Serialize handshake request
        /// </summary>
        byte[] SerializeHandshakeRequest(HandshakeRequest request);

        /// <summary>
        /// Deserialize handshake response
        /// </summary>
        HandshakeResponse DeserializeHandshakeResponse(byte[] data);
    }
}