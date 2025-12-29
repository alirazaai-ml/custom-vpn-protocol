using System;
using System.IO;
using System.Text.Json;

namespace VPN.Server
{
    /// <summary>
    /// Server configuration settings
    /// </summary>
    public class ServerConfiguration
    {
        // Network settings
        public string BindAddress { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 5000;
        public int MaxClients { get; set; } = 100;
        public int BufferSize { get; set; } = 4096;

        // Security settings
        public bool RequireAuthentication { get; set; } = true;
        public string AdminPassword { get; set; } = "admin123"; // Change in production!
        public bool EnableEncryption { get; set; } = true;

        // Timeout settings (milliseconds)
        public int HandshakeTimeout { get; set; } = 5000;
        public int KeepAliveInterval { get; set; } = 30000;
        public int SessionTimeout { get; set; } = 300000;

        // Logging
        public bool EnableLogging { get; set; } = true;
        public string LogFilePath { get; set; } = "vpn-server.log";

        // Performance
        public int MaxThreads { get; set; } = 10;
        public bool EnableCompression { get; set; } = false;

        /// <summary>
        /// Load configuration from JSON file
        /// </summary>
        public static ServerConfiguration LoadFromFile(string filePath = "server-config.json")
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<ServerConfiguration>(json)
                        ?? new ServerConfiguration();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not load config: {ex.Message}");
            }

            return new ServerConfiguration();
        }

        /// <summary>
        /// Save configuration to JSON file
        /// </summary>
        public void SaveToFile(string filePath = "server-config.json")
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
            if (Port < 1 || Port > 65535)
            {
                Console.WriteLine($"Error: Invalid port number: {Port}");
                return false;
            }

            if (MaxClients < 1 || MaxClients > 1000)
            {
                Console.WriteLine($"Error: Max clients must be between 1 and 1000");
                return false;
            }

            if (BufferSize < 1024 || BufferSize > 65535)
            {
                Console.WriteLine($"Error: Buffer size must be between 1024 and 65535");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Display configuration summary
        /// </summary>
        public void DisplaySummary()
        {
            Console.WriteLine("=== VPN Server Configuration ===");
            Console.WriteLine($"Bind Address: {BindAddress}");
            Console.WriteLine($"Port: {Port}");
            Console.WriteLine($"Max Clients: {MaxClients}");
            Console.WriteLine($"Require Auth: {RequireAuthentication}");
            Console.WriteLine($"Encryption: {EnableEncryption}");
            Console.WriteLine("================================");
        }
    }
}