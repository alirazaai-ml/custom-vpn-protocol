using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace VPN.Core.Models
{
    /// <summary>
    /// Connection configuration for VPN client
    /// </summary>
    public class ConnectionInfo
    {
        // Server configuration
        public string ServerIp { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 5000;

        // Client identification
        public string Username { get; set; } = "anonymous";
        public string ClientId { get; set; } = Guid.NewGuid().ToString().Substring(0, 8);

        // Security settings
        public string PreSharedKey { get; set; } = "default-key-change-me"; // For demo only!
        public bool UseEncryption { get; set; } = true;

        // Network settings
        public int BufferSize { get; set; } = 4096;
        public int ConnectionTimeout { get; set; } = 10000; // 10 seconds
        public int KeepAliveInterval { get; set; } = 30000; // 30 seconds

        // Calculated properties
        public IPEndPoint ServerEndPoint => new IPEndPoint(IPAddress.Parse(ServerIp), ServerPort);

        // Validation
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(ServerIp) &&
                   ServerPort > 0 && ServerPort < 65535 &&
                   !string.IsNullOrEmpty(Username);
        }
    }
}