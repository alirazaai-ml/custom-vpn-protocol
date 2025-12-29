using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using VPN.Core.Enums;

namespace VPN.Core.Models
{
    /// <summary>
    /// Server response to client handshake
    /// </summary>
    public class HandshakeResponse
    {
        [JsonPropertyName("version")]
        public byte Version { get; set; } = 0x01;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "success"; // "success", "error"

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = string.Empty;

        [JsonPropertyName("selected_encryption")]
        public EncryptionType SelectedEncryption { get; set; } = EncryptionType.AES256;

        [JsonPropertyName("keepalive_interval")]
        public int KeepAliveInterval { get; set; } = 30000;

        [JsonPropertyName("server_time")]
        public long ServerTime { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Error details if status is "error"
        [JsonPropertyName("error_code")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("retry_after")]
        public int RetryAfter { get; set; }
    }
}