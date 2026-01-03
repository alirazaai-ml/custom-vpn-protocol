using System;
using System.Net;
using System.Net.Sockets;

namespace VPN.Core.Protocol
{
    /// <summary>
    /// Parses IP packets to extract routing information
    /// Supports IPv4 packet parsing with full header extraction
    /// </summary>
    public class IpPacketParser
    {
        /// <summary>
        /// Parse an IP packet and extract routing information
        /// </summary>
        public static IpPacketInfo Parse(byte[] data)
        {
            if (data == null || data.Length < 20)
                return null;

            try
            {
                // Check if this is an IPv4 packet
                int version = (data[0] >> 4) & 0x0F;
                if (version != 4)
                    return null; // Only IPv4 supported for now

                var packetInfo = new IpPacketInfo();

                // Parse IPv4 header (minimum 20 bytes)
                int ihl = (data[0] & 0x0F) * 4; // Internet Header Length in bytes
                packetInfo.HeaderLength = ihl;

                // Total length
                packetInfo.TotalLength = (data[2] << 8) | data[3];

                // Protocol (6=TCP, 17=UDP, 1=ICMP)
                byte protocol = data[9];
                packetInfo.Protocol = protocol switch
                {
                    6 => ProtocolType.Tcp,
                    17 => ProtocolType.Udp,
                    1 => ProtocolType.Icmp,
                    _ => ProtocolType.Unknown
                };

                // Source IP (bytes 12-15)
                packetInfo.SourceIp = new IPAddress(new byte[] { data[12], data[13], data[14], data[15] });

                // Destination IP (bytes 16-19)
                packetInfo.DestinationIp = new IPAddress(new byte[] { data[16], data[17], data[18], data[19] });

                // Extract port information if TCP or UDP
                if (ihl < data.Length && (protocol == 6 || protocol == 17))
                {
                    // Source port (bytes ihl to ihl+1)
                    packetInfo.SourcePort = (data[ihl] << 8) | data[ihl + 1];

                    // Destination port (bytes ihl+2 to ihl+3)
                    packetInfo.DestinationPort = (data[ihl + 2] << 8) | data[ihl + 3];
                }

                // Payload starts after IP header
                if (data.Length > ihl)
                {
                    packetInfo.Payload = new byte[data.Length - ihl];
                    Buffer.BlockCopy(data, ihl, packetInfo.Payload, 0, data.Length - ihl);
                }
                else
                {
                    packetInfo.Payload = Array.Empty<byte>();
                }

                // Store full packet for reconstruction
                packetInfo.FullPacket = data;

                return packetInfo;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Try to detect HTTP/HTTPS traffic for special handling
        /// </summary>
        public static bool IsHttpTraffic(byte[] data)
        {
            if (data == null || data.Length < 5)
                return false;

            try
            {
                string start = System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(5, data.Length));
                return start.StartsWith("GET ") || 
                       start.StartsWith("POST ") || 
                       start.StartsWith("PUT ") ||
                       start.StartsWith("DELE") || // DELETE
                       start.StartsWith("HEAD") ||
                       start.StartsWith("HTTP/");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Create a simple IP packet for testing
        /// </summary>
        public static byte[] CreateSimplePacket(IPAddress source, IPAddress destination, 
                                                ProtocolType protocol, byte[] payload)
        {
            // Simplified packet creation for testing
            int headerSize = 20;
            int totalSize = headerSize + payload.Length;
            byte[] packet = new byte[totalSize];

            // Version (4) and IHL (5 = 20 bytes)
            packet[0] = 0x45;

            // Total length
            packet[2] = (byte)(totalSize >> 8);
            packet[3] = (byte)(totalSize & 0xFF);

            // Protocol
            packet[9] = protocol switch
            {
                ProtocolType.Tcp => 6,
                ProtocolType.Udp => 17,
                ProtocolType.Icmp => 1,
                _ => 0
            };

            // Source IP
            byte[] srcBytes = source.GetAddressBytes();
            Buffer.BlockCopy(srcBytes, 0, packet, 12, 4);

            // Destination IP
            byte[] dstBytes = destination.GetAddressBytes();
            Buffer.BlockCopy(dstBytes, 0, packet, 16, 4);

            // Payload
            Buffer.BlockCopy(payload, 0, packet, headerSize, payload.Length);

            return packet;
        }
    }

    /// <summary>
    /// Parsed IP packet information
    /// </summary>
    public class IpPacketInfo
    {
        public int HeaderLength { get; set; }
        public int TotalLength { get; set; }
        public ProtocolType Protocol { get; set; }
        public IPAddress SourceIp { get; set; }
        public IPAddress DestinationIp { get; set; }
        public int SourcePort { get; set; }
        public int DestinationPort { get; set; }
        public byte[] Payload { get; set; }
        public byte[] FullPacket { get; set; }

        public override string ToString()
        {
            return $"{Protocol} {SourceIp}:{SourcePort} ? {DestinationIp}:{DestinationPort}";
        }
    }
}
