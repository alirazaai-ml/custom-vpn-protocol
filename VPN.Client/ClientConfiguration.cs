using System;
using System.IO;
using System.Text.Json;

namespace VPN.Client
{
    /// <summary>
    /// Client configuration settings
    /// </summary>
    public class ClientConfiguration
    {
        // Server connection
        public string ServerIp { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 5000;

        // Client identification
        public string ClientId { get; set; } = $"client-{Guid.NewGuid().ToString().Substring(0, 8)}";
        public string Username { get; set; } = "user";
        public string Password { get; set; } = ""; // Leave empty for no auth

        // Network settings
        public int BufferSize { get; set; } = 4096;
        public int ConnectionTimeout { get; set; } = 10000;
        public int KeepAliveInterval { get; set; } = 30000;
        public int ReconnectDelay { get; set; } = 5000;

        // Security
        public bool EnableEncryption { get; set; } = true;
        public string PreSharedKey { get; set; } = "default-key-change-me";

        // Tunnel settings
        public bool AutoConnect { get; set; } = false;
        public bool EnableLocalProxy { get; set; } = true;
        public int LocalProxyPort { get; set; } = 1080; // SOCKS proxy port
        public string TunnelInterface { get; set; } = "VPN-Tunnel";

        // Logging
        public bool EnableLogging { get; set; } = true;
        public string LogFilePath { get; set; } = "vpn-client.log";

        /// <summary>
        /// Load configuration from JSON file
        /// </summary>
        public static ClientConfiguration LoadFromFile(string filePath = "client-config.json")
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<ClientConfiguration>(json)
                        ?? new ClientConfiguration();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not load config: {ex.Message}");
            }

            return new ClientConfiguration();
        }

        /// <summary>
        /// Save configuration to JSON file
        /// </summary>
        public void SaveToFile(string filePath = "client-config.json")
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not save config: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate configuration
        /// </summary>
        public bool Validate()
        {
            if (string.IsNullOrEmpty(ServerIp))
            {
                Console.WriteLine("Error: Server IP is required");
                return false;
            }

            if (ServerPort < 1 || ServerPort > 65535)
            {
                Console.WriteLine($"Error: Invalid port number: {ServerPort}");
                return false;
            }

            if (BufferSize < 1024 || BufferSize > 65535)
            {
                Console.WriteLine($"Error: Buffer size must be between 1024 and 65535");
                return false;
            }

            if (LocalProxyPort < 1 || LocalProxyPort > 65535)
            {
                Console.WriteLine($"Error: Invalid local proxy port: {LocalProxyPort}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Display configuration summary
        /// </summary>
        public void DisplaySummary()
        {
            Console.WriteLine("=== VPN Client Configuration ===");
            Console.WriteLine($"Server: {ServerIp}:{ServerPort}");
            Console.WriteLine($"Client ID: {ClientId}");
            Console.WriteLine($"Encryption: {EnableEncryption}");
            Console.WriteLine($"Local Proxy: {EnableLocalProxy} (Port: {LocalProxyPort})");
            Console.WriteLine($"Auto Connect: {AutoConnect}");
            Console.WriteLine("================================");
        }

        /// <summary>
        /// Create a new configuration with defaults
        /// </summary>
        public static ClientConfiguration CreateDefault(string serverIp = "127.0.0.1", int port = 5000)
        {
            return new ClientConfiguration
            {
                ServerIp = serverIp,
                ServerPort = port
            };
        }
    }
}