using System;
using System.Collections.Concurrent;
using System.Net;

namespace VPN.Server
{
    /// <summary>
    /// Network Address Translation (NAT) Manager
    /// Maps client internal IPs to server's external IP
    /// </summary>
    public class NatManager
    {
        // NAT translation table: (sessionId, originalSourceIP, originalSourcePort) -> (translatedPort)
        private readonly ConcurrentDictionary<string, NatEntry> _natTable;
        
        // Reverse lookup: translatedPort -> NatEntry
        private readonly ConcurrentDictionary<int, NatEntry> _reverseNatTable;
        
        // Server's external IP address
        private IPAddress _serverExternalIp;
        
        // Port pool for NAT translations (49152-65535 dynamic range)
        private int _nextAvailablePort = 49152;
        private readonly object _portLock = new object();

        public NatManager()
        {
            _natTable = new ConcurrentDictionary<string, NatEntry>();
            _reverseNatTable = new ConcurrentDictionary<int, NatEntry>();
            
            // Auto-detect server's external IP
            _serverExternalIp = GetServerExternalIp();
        }

        /// <summary>
        /// Translate outgoing packet (client -> internet)
        /// Replaces source IP with server IP and assigns NAT port
        /// </summary>
        public NatTranslation TranslateOutgoing(string sessionId, IPAddress clientIp, int clientPort, 
                                                 IPAddress destinationIp, int destinationPort)
        {
            // Create unique key for this connection
            string natKey = $"{sessionId}_{clientIp}_{clientPort}_{destinationIp}_{destinationPort}";

            // Check if translation already exists
            if (_natTable.TryGetValue(natKey, out var existing))
            {
                return new NatTranslation
                {
                    TranslatedSourceIp = _serverExternalIp,
                    TranslatedSourcePort = existing.TranslatedPort,
                    OriginalSourceIp = clientIp,
                    OriginalSourcePort = clientPort
                };
            }

            // Create new NAT entry
            int translatedPort = AllocatePort();
            var entry = new NatEntry
            {
                SessionId = sessionId,
                OriginalSourceIp = clientIp,
                OriginalSourcePort = clientPort,
                DestinationIp = destinationIp,
                DestinationPort = destinationPort,
                TranslatedPort = translatedPort,
                CreatedAt = DateTime.UtcNow,
                LastUsed = DateTime.UtcNow
            };

            _natTable[natKey] = entry;
            _reverseNatTable[translatedPort] = entry;

            return new NatTranslation
            {
                TranslatedSourceIp = _serverExternalIp,
                TranslatedSourcePort = translatedPort,
                OriginalSourceIp = clientIp,
                OriginalSourcePort = clientPort
            };
        }

        /// <summary>
        /// Translate incoming packet (internet -> client)
        /// Uses NAT table to find original client address
        /// </summary>
        public NatTranslation TranslateIncoming(int destinationPort)
        {
            if (_reverseNatTable.TryGetValue(destinationPort, out var entry))
            {
                entry.LastUsed = DateTime.UtcNow;

                return new NatTranslation
                {
                    TranslatedSourceIp = entry.DestinationIp,
                    TranslatedSourcePort = entry.DestinationPort,
                    OriginalSourceIp = entry.OriginalSourceIp,
                    OriginalSourcePort = entry.OriginalSourcePort,
                    SessionId = entry.SessionId
                };
            }

            return null; // No NAT entry found
        }

        /// <summary>
        /// Clean up NAT entries for a disconnected session
        /// </summary>
        public void RemoveSession(string sessionId)
        {
            var entriesToRemove = _natTable.Where(kvp => kvp.Value.SessionId == sessionId).ToList();
            
            foreach (var entry in entriesToRemove)
            {
                _natTable.TryRemove(entry.Key, out _);
                _reverseNatTable.TryRemove(entry.Value.TranslatedPort, out _);
            }

            Console.WriteLine($"[NAT] Cleaned up {entriesToRemove.Count} entries for session {sessionId}");
        }

        /// <summary>
        /// Clean up expired NAT entries (older than timeout)
        /// </summary>
        public void CleanupExpired(int timeoutSeconds = 300)
        {
            var now = DateTime.UtcNow;
            var expiredEntries = _natTable.Where(kvp => 
                (now - kvp.Value.LastUsed).TotalSeconds > timeoutSeconds).ToList();

            foreach (var entry in expiredEntries)
            {
                _natTable.TryRemove(entry.Key, out _);
                _reverseNatTable.TryRemove(entry.Value.TranslatedPort, out _);
            }

            if (expiredEntries.Count > 0)
            {
                Console.WriteLine($"[NAT] Cleaned up {expiredEntries.Count} expired entries");
            }
        }

        /// <summary>
        /// Allocate a new NAT port
        /// </summary>
        private int AllocatePort()
        {
            lock (_portLock)
            {
                int port = _nextAvailablePort++;
                
                // Wrap around if we exceed the dynamic port range
                if (_nextAvailablePort > 65535)
                {
                    _nextAvailablePort = 49152;
                }

                return port;
            }
        }

        /// <summary>
        /// Auto-detect server's external IP address
        /// </summary>
        private IPAddress GetServerExternalIp()
        {
            try
            {
                // Try to get the first non-loopback IPv4 address
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip))
                    {
                        Console.WriteLine($"[NAT] Server external IP: {ip}");
                        return ip;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NAT] Error detecting IP: {ex.Message}");
            }

            // Fallback to localhost
            return IPAddress.Loopback;
        }

        /// <summary>
        /// Get NAT statistics
        /// </summary>
        public (int totalEntries, int activeEntries) GetStatistics()
        {
            int total = _natTable.Count;
            int active = _natTable.Count(kvp => 
                (DateTime.UtcNow - kvp.Value.LastUsed).TotalSeconds < 60);
            
            return (total, active);
        }

        /// <summary>
        /// Get server's external IP
        /// </summary>
        public IPAddress GetServerIp() => _serverExternalIp;
    }

    /// <summary>
    /// NAT table entry
    /// </summary>
    public class NatEntry
    {
        public string SessionId { get; set; }
        public IPAddress OriginalSourceIp { get; set; }
        public int OriginalSourcePort { get; set; }
        public IPAddress DestinationIp { get; set; }
        public int DestinationPort { get; set; }
        public int TranslatedPort { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUsed { get; set; }
    }

    /// <summary>
    /// NAT translation result
    /// </summary>
    public class NatTranslation
    {
        public IPAddress TranslatedSourceIp { get; set; }
        public int TranslatedSourcePort { get; set; }
        public IPAddress OriginalSourceIp { get; set; }
        public int OriginalSourcePort { get; set; }
        public string SessionId { get; set; }
    }
}
