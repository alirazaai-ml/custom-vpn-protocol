using System;
using System.Net;
using VPN.Core.Enums;

namespace VPN.Core.Models
{
    /// <summary>
    /// Represents a VPN client session on the server
    /// </summary>
    public class Session
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public string ClientId { get; set; } = string.Empty;
        public IPEndPoint ClientEndPoint { get; set; } = null!;

        // Connection info
        public ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivity { get; set; }

        // Security
        public byte[] SessionKey { get; set; } = Array.Empty<byte>();
        public byte[] Iv { get; set; } = Array.Empty<byte>();
        public EncryptionType EncryptionType { get; set; } = EncryptionType.AES256;

        // Statistics
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public int PacketsSent { get; set; }
        public int PacketsReceived { get; set; }

        // Time tracking
        public TimeSpan Uptime => DateTime.UtcNow - ConnectedAt;
        public TimeSpan IdleTime => DateTime.UtcNow - LastActivity;

        // Methods
        public void UpdateActivity()
        {
            LastActivity = DateTime.UtcNow;
        }

        public bool IsExpired(int timeoutSeconds = 300)
        {
            return IdleTime.TotalSeconds > timeoutSeconds;
        }
    }
}