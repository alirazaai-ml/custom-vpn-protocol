using System;
using System.Collections.Generic;
using System.Text;

namespace VPN.Core.Protocol
{
    /// <summary>
    /// Constants for VPN protocol configuration
    /// </summary>
    public static class ProtocolConstants
    {
        // Network settings
        public const int DEFAULT_PORT = 5000;
        public const int MAX_PACKET_SIZE = 65535;      // Max UDP packet size
        public const int BUFFER_SIZE = 4096;           // Read/write buffer size
        public const int MAX_CLIENTS = 100;            // Maximum simultaneous clients

        // Protocol versions
        public const byte PROTOCOL_VERSION = 0x01;

        // Timeouts (in milliseconds)
        public const int CONNECT_TIMEOUT = 10000;      // 10 seconds
        public const int HANDSHAKE_TIMEOUT = 5000;     // 5 seconds
        public const int KEEPALIVE_INTERVAL = 30000;   // 30 seconds
        public const int SESSION_TIMEOUT = 300000;     // 5 minutes

        // Packet header sizes
        public const int HEADER_SIZE = 16;             // Base header size in bytes
        public const int HMAC_SIZE = 32;               // SHA256 HMAC size

        // Magic numbers for protocol identification
        public static readonly byte[] MAGIC_HEADER = { 0x56, 0x50, 0x4E, 0x01 }; // "VPN" + version

        // Error codes
        public const int ERROR_AUTH_FAILED = 1001;
        public const int ERROR_INVALID_PACKET = 1002;
        public const int ERROR_SESSION_EXPIRED = 1003;
        public const int ERROR_ENCRYPTION_FAILED = 1004;
        public const int ERROR_SERVER_FULL = 1005;

        // Status messages
        public const string STATUS_SUCCESS = "success";
        public const string STATUS_ERROR = "error";
        public const string STATUS_AUTH_REQUIRED = "auth_required";

        // Default encryption settings
        public const string DEFAULT_CIPHER = "AES256-CBC";
        public const string DEFAULT_HASH = "SHA256";
    }
}
