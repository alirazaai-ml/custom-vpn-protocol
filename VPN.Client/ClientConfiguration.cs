using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace VPN.Client
{
    /// <summary>
    /// Client configuration settings with auto-detection
    /// </summary>
    public class ClientConfiguration
    {
        // AUTO-DETECTED SERVER SETTINGS
        //public string ServerIp { get; set; } = "127.0.0.1"; // Default to localhost
        public string ServerIp { get; set; } = "YOUR_SERVER_IP_HERE"; // Use the IP shown in server logs
        public int ServerPort { get; set; } = 5000; // Fixed port

        // Client identification - ONLY username required from user
        public string ClientId { get; set; } = $"client-{Guid.NewGuid().ToString().Substring(0, 8)}";
        public string Username { get; set; } = ""; // User enters this only
        public string Password { get; set; } = ""; // NOT USED - kept for compatibility

        // Network settings
        public int BufferSize { get; set; } = 4096;
        public int ConnectionTimeout { get; set; } = 10000;
        public int KeepAliveInterval { get; set; } = 30000;
        public int ReconnectDelay { get; set; } = 5000;

        // Security
        public bool EnableEncryption { get; set; } = true; // Always encrypted
        public string PreSharedKey { get; set; } = "default-key-change-me";

        // Tunnel settings
        public bool AutoConnect { get; set; } = false;
        public bool EnableLocalProxy { get; set; } = true;
        public int LocalProxyPort { get; set; } = 1080; // SOCKS proxy port
        public string TunnelInterface { get; set; } = "VPN-Tunnel";

        // System proxy auto-configuration
        public bool AutoConfigureSystemProxy { get; set; } = true; // Auto-set Windows proxy

        // Logging
        public bool EnableLogging { get; set; } = true;
        public string LogFilePath { get; set; } = "vpn-client.log";

        public ClientConfiguration()
        {
            // Auto-detect best server IP on creation
            AutoDetectServerIp();
        }

        /// <summary>
        /// Auto-detect the best server IP to connect to
        /// </summary>
        private void AutoDetectServerIp()
        {
            try
            {
                // First try localhost
                if (TestConnection("127.0.0.1", ServerPort))
                {
                    ServerIp = "127.0.0.1";
                    Console.WriteLine($"✅ Auto-detected: Server is on localhost (127.0.0.1)");
                    return;
                }

                // Try to find local network IPs
                string localIp = GetLocalNetworkIp();
                if (!string.IsNullOrEmpty(localIp) && TestConnection(localIp, ServerPort))
                {
                    ServerIp = localIp;
                    Console.WriteLine($"✅ Auto-detected: Server is on local network at {localIp}");
                    return;
                }

                Console.WriteLine($"⚠️  Could not auto-detect server. Using default: {ServerIp}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Server auto-detection failed: {ex.Message}. Using default: {ServerIp}");
            }
        }

        /// <summary>
        /// Test if server is reachable at given IP
        /// </summary>
        private bool TestConnection(string ip, int port, int timeout = 2000)
        {
            try
            {
                using var tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(timeout);

                if (Task.WhenAny(connectTask, timeoutTask).Result == timeoutTask)
                {
                    return false; // Timeout
                }

                connectTask.Wait(); // Ensure connection completed
                return tcpClient.Connected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get local network IP (e.g., 192.168.x.x)
        /// </summary>
        private string GetLocalNetworkIp()
        {
            try
            {
                string hostName = Dns.GetHostName();
                IPAddress[] addresses = Dns.GetHostAddresses(hostName);

                foreach (IPAddress address in addresses)
                {
                    // Check for IPv4 local network addresses
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipString = address.ToString();
                        if (ipString.StartsWith("192.168.") ||
                            ipString.StartsWith("10.") ||
                            ipString.StartsWith("172."))
                        {
                            return ipString;
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        /// <summary>
        /// Get the default config file path in user's AppData
        /// </summary>
        private static string GetDefaultConfigPath()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string vpnFolder = Path.Combine(appDataPath, "VPN-Solution");

            // Create directory if it doesn't exist
            if (!Directory.Exists(vpnFolder))
            {
                Directory.CreateDirectory(vpnFolder);
            }

            return Path.Combine(vpnFolder, "client-config.json");
        }

        /// <summary>
        /// Load configuration from JSON file
        /// </summary>
        public static ClientConfiguration LoadFromFile(string filePath = null)
        {
            // Use AppData path if no path specified
            filePath = filePath ?? GetDefaultConfigPath();

            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var config = JsonSerializer.Deserialize<ClientConfiguration>(json)
                        ?? new ClientConfiguration();
                    Console.WriteLine($"✅ Configuration loaded from: {filePath}");

                    // Re-run auto-detection to ensure we have the right IP
                    config.AutoDetectServerIp();

                    return config;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not load config: {ex.Message}");
            }

            Console.WriteLine("Using default configuration...");
            return new ClientConfiguration();
        }

        /// <summary>
        /// Save configuration to JSON file
        /// </summary>
        public void SaveToFile(string filePath = null)
        {
            // Use AppData path if no path specified
            filePath = filePath ?? GetDefaultConfigPath();

            try
            {
                // Ensure directory exists
                string directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
                Console.WriteLine($"✅ Configuration saved to: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not save config: {ex.Message}");
                Console.WriteLine("Client will continue with current settings...");
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