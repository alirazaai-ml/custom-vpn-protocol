using System;
using System.Collections.Generic;
using System.Text;

namespace VPN.Core.Enums
{
    /// <summary>
    /// Defines types of packets in our custom VPN protocol
    /// </summary>
    public enum PacketType : byte
    {
        // Control packets
        HandshakeRequest = 0x01,      // Client initiates connection
        HandshakeResponse = 0x02,     // Server responds to handshake
        Authentication = 0x03,        // Authentication credentials
        KeepAlive = 0x04,             // Keep connection alive
        Disconnect = 0x05,            // Graceful disconnect

        // Data packets  
        Data = 0x10,                  // Encrypted application data
        TunnelData = 0x11,            // Tunneled IP packets

        // Error packets
        Error = 0xFF,                  // Error notification

        // DNS packets
        DnsQuery = 0x12,             // DNS query to be resolved
        DnsResponse = 0x13            // DNS query response
    }
}