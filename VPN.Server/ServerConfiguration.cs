using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VPN.Core.Security;

namespace VPN.Server
{
    /// <summary>
    /// Approved user information
    /// </summary>
    public class ApprovedUser
    {
        public string Username { get; set; } = string.Empty;
        public DateTime ApprovedAt { get; set; }
        public DateTime LastConnected { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public int TotalConnections { get; set; }
    }

    /// <summary>
    /// Secure server configuration with production-ready defaults
    /// </summary>
    public class ServerConfiguration
    {
        // Network settings
        public string BindAddress { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 5000;
        public int MaxClients { get; set; } = 50;
        public int BufferSize { get; set; } = 4096;

        // User approval settings
        public List<ApprovedUser> ApprovedUsers { get; set; } = new List<ApprovedUser>();
        public bool RequireApproval { get; set; } = true; // First-time approval required

        // Security settings
        public bool RequireAuthentication { get; set; } = false; // No password needed, username only
        public string AdminPasswordHash { get; set; } = string.Empty;
        public string AdminPasswordSalt { get; set; } = string.Empty;
        public bool EnableEncryption { get; set; } = true;
        public bool ForcePasswordChange { get; set; } = false;

        // Rate limiting (DDoS protection)
        public int MaxConnectionsPerMinute { get; set; } = 10;
        public int MaxConnectionsPerIP { get; set; } = 5;
        public bool EnableRateLimiting { get; set; } = true;

        // Timeout settings (milliseconds)
        public int HandshakeTimeout { get; set; } = 5000;
        public int KeepAliveInterval { get; set; } = 30000;
        public int SessionTimeout { get; set; } = 300000;

        // Logging
        public bool EnableLogging { get; set; } = true;
        public string LogFilePath { get; set; } = "vpn-server.log";
        public bool LogSensitiveData { get; set; } = false; // Never log passwords!

        // Performance
        public int MaxThreads { get; set; } = 10;
        public bool EnableCompression { get; set; } = false;

        // Security flags
        public bool EnableIPWhitelist { get; set; } = false;
        public string[] AllowedIPs { get; set; } = Array.Empty<string>();
        public bool EnableClientCertificate { get; set; } = false;
        public string CertificatePath { get; set; } = string.Empty;

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
            
            return Path.Combine(vpnFolder, "server-config.json");
        }

        /// <summary>
        /// Load configuration from JSON file with secure defaults
        /// </summary>
        public static ServerConfiguration LoadFromFile(string filePath = null)
        {
            // Use AppData path if no path specified
            filePath = filePath ?? GetDefaultConfigPath();
            
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var config = JsonSerializer.Deserialize<ServerConfiguration>(json)
                        ?? new ServerConfiguration();

                    // If password is still in plain text (old format), hash it
                    if (!string.IsNullOrEmpty(config.AdminPasswordHash) &&
                        config.AdminPasswordHash.Length < 64) // Likely plain text
                    {
                        Console.WriteLine("⚠️ WARNING: Plain text password detected in config!");
                        Console.WriteLine("Regenerating secure password hash...");
                        config.GenerateSecurePassword("ChangeMe123!"); // Default strong password
                        config.SaveToFile(filePath);
                    }

                    Console.WriteLine($"✅ Configuration loaded from: {filePath}");
                    return config;
                }
                else
                {
                    // Create new config with secure defaults
                    var newConfig = new ServerConfiguration();

                    // Generate secure admin password on first run
                    string randomPassword = GenerateRandomPassword(16);
                    newConfig.GenerateSecurePassword(randomPassword);

                    Console.WriteLine("===============================================");
                    Console.WriteLine("🔒 FIRST TIME SETUP - SECURE ADMIN PASSWORD");
                    Console.WriteLine("===============================================");
                    Console.WriteLine($"Admin Password: {randomPassword}");
                    Console.WriteLine("===============================================");
                    Console.WriteLine("⚠️  IMPORTANT: Save this password immediately!");
                    Console.WriteLine("⚠️  You won't see it again!");
                    Console.WriteLine("⚠️  Change it after first login!");
                    Console.WriteLine("===============================================");
                    Console.WriteLine($"Config will be saved to: {filePath}");
                    Console.WriteLine("===============================================");

                    newConfig.SaveToFile(filePath);
                    return newConfig;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Warning: Could not load config: {ex.Message}");
                Console.WriteLine("Using secure defaults without saving...");
                var config = new ServerConfiguration();
                // Generate password but don't require save
                config.GenerateSecurePassword("TempPassword123!");
                return config;
            }
        }

        /// <summary>
        /// Save configuration to JSON file (without sensitive data)
        /// </summary>
        public void SaveToFile(string filePath = null)
        {
            // Use AppData path if no path specified
            filePath = filePath ?? GetDefaultConfigPath();
            
            try
            {
                // Never save plain text passwords
                if (!string.IsNullOrEmpty(this.AdminPasswordHash) && this.AdminPasswordHash.Length < 64)
                {
                    throw new InvalidOperationException(
                        "Attempted to save plain text password! Use GenerateSecurePassword() first.");
                }

                // Ensure directory exists
                string directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    // Don't serialize null values
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };

                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(filePath, json);

                Console.WriteLine($"✅ Configuration saved to: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error saving config: {ex.Message}");
                Console.WriteLine("Server will continue with current settings...");
                // Don't throw - allow server to run with defaults
            }
        }

        /// <summary>
        /// Generate secure password hash and salt
        /// </summary>
        public void GenerateSecurePassword(string plainTextPassword)
        {
            if (string.IsNullOrWhiteSpace(plainTextPassword))
                throw new ArgumentException("Password cannot be empty");

            if (plainTextPassword.Length < 12)
                throw new ArgumentException("Password must be at least 12 characters");

            // Generate random salt
            byte[] salt = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Use the better PBKDF2 implementation
            byte[] hash = HashHelper.HashPasswordWithSHA256(
                plainTextPassword,
                salt,
                600000); // OWASP 2023 recommendation

            // Store as base64 strings
            AdminPasswordSalt = Convert.ToBase64String(salt);
            AdminPasswordHash = Convert.ToBase64String(hash);
            ForcePasswordChange = false;
        }

        /// <summary>
        /// Verify a password against stored hash
        /// </summary>
        public bool VerifyPassword(string plainTextPassword)
        {
            if (string.IsNullOrEmpty(AdminPasswordHash) || string.IsNullOrEmpty(AdminPasswordSalt))
                return false;

            try
            {
                byte[] salt = Convert.FromBase64String(AdminPasswordSalt);
                byte[] storedHash = Convert.FromBase64String(AdminPasswordHash);

                byte[] computedHash = HashHelper.HashPasswordWithSHA256(
                    plainTextPassword,
                    salt,
                    600000);

                return CryptographicOperations.FixedTimeEquals(storedHash, computedHash);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if user is approved
        /// </summary>
        public bool IsUserApproved(string username)
        {
            return ApprovedUsers.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Check if username is already taken
        /// </summary>
        public bool IsUsernameTaken(string username)
        {
            return ApprovedUsers.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Add approved user
        /// </summary>
        public void ApproveUser(string username, string clientId)
        {
            if (!IsUserApproved(username))
            {
                ApprovedUsers.Add(new ApprovedUser
                {
                    Username = username,
                    ClientId = clientId,
                    ApprovedAt = DateTime.Now,
                    LastConnected = DateTime.Now,
                    TotalConnections = 1
                });
                SaveToFile();
            }
        }

        /// <summary>
        /// Update user last connected time
        /// </summary>
        public void UpdateUserConnection(string username)
        {
            var user = ApprovedUsers.FirstOrDefault(u => 
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            
            if (user != null)
            {
                user.LastConnected = DateTime.Now;
                user.TotalConnections++;
                SaveToFile();
            }
        }

        /// <summary>
        /// Remove approved user
        /// </summary>
        public void RemoveUser(string username)
        {
            ApprovedUsers.RemoveAll(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            SaveToFile();
        }

        /// <summary>
        /// Validate configuration for security
        /// </summary>
        public bool Validate()
        {
            var errors = new System.Collections.Generic.List<string>();

            // Port validation
            if (Port < 1024 || Port > 65535)
                errors.Add($"Port must be between 1024 and 65535 (got {Port})");

            // Max clients validation
            if (MaxClients < 1 || MaxClients > 500)
                errors.Add($"Max clients must be between 1 and 500 (got {MaxClients})");

            // Buffer size validation
            if (BufferSize < 1024 || BufferSize > 65535)
                errors.Add($"Buffer size must be between 1024 and 65535 (got {BufferSize})");

            // Password validation (if authentication is required)
            if (RequireAuthentication)
            {
                if (string.IsNullOrEmpty(AdminPasswordHash))
                    errors.Add("Admin password hash is not set");

                if (string.IsNullOrEmpty(AdminPasswordSalt))
                    errors.Add("Admin password salt is not set");
            }

            // Rate limiting validation
            if (EnableRateLimiting)
            {
                if (MaxConnectionsPerMinute < 1)
                    errors.Add("Max connections per minute must be at least 1");

                if (MaxConnectionsPerIP < 1)
                    errors.Add("Max connections per IP must be at least 1");
            }

            // Log errors
            if (errors.Count > 0)
            {
                Console.WriteLine("❌ Configuration validation failed:");
                foreach (var error in errors)
                    Console.WriteLine($"   - {error}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Display secure configuration summary (no sensitive data)
        /// </summary>
        public void DisplaySummary()
        {
            Console.WriteLine("==========================================");
            Console.WriteLine("🔒 VPN SERVER CONFIGURATION");
            Console.WriteLine("==========================================");
            Console.WriteLine($"Bind Address: {BindAddress}");
            Console.WriteLine($"Port: {Port}");
            Console.WriteLine($"Max Clients: {MaxClients}");
            Console.WriteLine($"Require Auth: {RequireAuthentication}");
            Console.WriteLine($"Encryption: {(EnableEncryption ? "✅ Enabled (AES-256)" : "❌ Disabled")}");
            Console.WriteLine($"Rate Limiting: {(EnableRateLimiting ? "✅ Enabled" : "❌ Disabled")}");

            if (EnableRateLimiting)
            {
                Console.WriteLine($"   Max/Min: {MaxConnectionsPerMinute}");
                Console.WriteLine($"   Max/IP: {MaxConnectionsPerIP}");
            }

            Console.WriteLine($"Session Timeout: {SessionTimeout / 1000}s");
            Console.WriteLine($"Password Set: {(string.IsNullOrEmpty(AdminPasswordHash) ? "❌ No" : "✅ Yes")}");
            Console.WriteLine($"Force Password Change: {(ForcePasswordChange ? "✅ Yes" : "❌ No")}");
            Console.WriteLine("==========================================");
        }

        /// <summary>
        /// Generate cryptographically random password
        /// </summary>
        private static string GenerateRandomPassword(int length)
        {
            const string validChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%^&*";
            char[] chars = new char[length];

            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] randomBytes = new byte[length];
                rng.GetBytes(randomBytes);

                for (int i = 0; i < length; i++)
                {
                    chars[i] = validChars[randomBytes[i] % validChars.Length];
                }
            }

            return new string(chars);
        }

        /// <summary>
        /// Check if this is first run (no password set)
        /// </summary>
        public bool IsFirstRun()
        {
            return string.IsNullOrEmpty(AdminPasswordHash) || ForcePasswordChange;
        }

        /// <summary>
        /// Get server's external IP address (for client connections)
        /// </summary>
        public static string GetServerIpAddress()
        {
            try
            {
                // Get all network interfaces
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                foreach (var ni in interfaces)
                {
                    var properties = ni.GetIPProperties();
                    var addresses = properties.UnicastAddresses
                        .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                                    !IPAddress.IsLoopback(ua.Address))
                        .ToList();

                    if (addresses.Any())
                    {
                        // Prefer private network addresses (192.168.x.x, 10.x.x.x, 172.16-31.x.x)
                        var privateAddress = addresses.FirstOrDefault(ua => IsPrivateIp(ua.Address));
                        if (privateAddress != null)
                        {
                            return privateAddress.Address.ToString();
                        }

                        // Otherwise use first available
                        return addresses.First().Address.ToString();
                    }
                }

                // Fallback: Try DNS method
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a => 
                    a.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(a));

                if (ip != null)
                    return ip.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error detecting server IP: {ex.Message}");
            }

            // Ultimate fallback
            return "127.0.0.1";
        }

        /// <summary>
        /// Check if IP is in private range
        /// </summary>
        private static bool IsPrivateIp(IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();
            
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;
            
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;
            
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
            
            return false;
        }

        /// <summary>
        /// Get all available network interfaces with IP addresses
        /// </summary>
        public static System.Collections.Generic.List<NetworkInterfaceInfo> GetNetworkInterfaces()
        {
            var result = new System.Collections.Generic.List<NetworkInterfaceInfo>();

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        var properties = ni.GetIPProperties();
                        var addresses = properties.UnicastAddresses
                            .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            .Select(ua => ua.Address.ToString())
                            .ToList();

                        if (addresses.Any())
                        {
                            result.Add(new NetworkInterfaceInfo
                            {
                                Name = ni.Name,
                                Description = ni.Description,
                                Type = ni.NetworkInterfaceType.ToString(),
                                IpAddresses = addresses,
                                IsUp = ni.OperationalStatus == OperationalStatus.Up,
                                Speed = ni.Speed
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enumerating network interfaces: {ex.Message}");
            }

            return result;
        }
    }

    /// <summary>
    /// Network interface information for display
    /// </summary>
    public class NetworkInterfaceInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public System.Collections.Generic.List<string> IpAddresses { get; set; }
        public bool IsUp { get; set; }
        public long Speed { get; set; }
    }
}