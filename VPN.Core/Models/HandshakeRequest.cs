using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace VPN.Core.Models
{
    /// <summary>
    /// Initial handshake request from client to server
    /// </summary>
    public class HandshakeRequest
    {
        [JsonPropertyName("version")]
        public byte Version { get; set; } = 0x01;

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("encryption_supported")]
        public string[] SupportedEncryption { get; set; } = { "AES256", "AES128" };

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // For future use
        [JsonPropertyName("features")]
        public string[] Features { get; set; } = { "tunneling", "encryption" };
    }
}