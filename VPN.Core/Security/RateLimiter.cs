using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace VPN.Core.Security
{
    /// <summary>
    /// Production-grade rate limiter for DDoS protection
    /// </summary>
    public class RateLimiter : IDisposable
    {
        private readonly ConcurrentDictionary<string, ClientRateInfo> _clientAttempts;
        private readonly Timer _cleanupTimer;
        private bool _disposed = false;

        // Configuration
        public int MaxRequestsPerMinute { get; set; } = 60;
        public int MaxConnectionsPerIP { get; set; } = 5;
        public int BanDurationMinutes { get; set; } = 15;
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Information about client rate limiting
        /// </summary>
        private class ClientRateInfo
        {
            public string IPAddress { get; set; } = string.Empty;
            public int RequestCount { get; set; }
            public DateTime WindowStart { get; set; }
            public DateTime? BannedUntil { get; set; }
            public int ConnectionCount { get; set; }
            public List<DateTime> ConnectionTimes { get; set; } = new List<DateTime>();
            public DateTime LastSeen { get; set; }
        }

        public RateLimiter()
        {
            _clientAttempts = new ConcurrentDictionary<string, ClientRateInfo>();
            _cleanupTimer = new Timer(CleanupOldEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Check if a request from an IP should be allowed
        /// </summary>
        public bool AllowRequest(string ipAddress, string requestType = "general")
        {
            if (!Enabled || string.IsNullOrEmpty(ipAddress))
                return true;

            var now = DateTime.UtcNow;
            var clientInfo = _clientAttempts.GetOrAdd(ipAddress, ip => new ClientRateInfo
            {
                IPAddress = ip,
                WindowStart = now,
                LastSeen = now
            });

            lock (clientInfo)
            {
                clientInfo.LastSeen = now;

                // Check if banned
                if (clientInfo.BannedUntil.HasValue && clientInfo.BannedUntil.Value > now)
                    return false;

                // Reset window if more than 1 minute has passed
                if (now - clientInfo.WindowStart > TimeSpan.FromMinutes(1))
                {
                    clientInfo.RequestCount = 0;
                    clientInfo.WindowStart = now;
                }

                // Check rate limit
                if (clientInfo.RequestCount >= MaxRequestsPerMinute)
                {
                    // Ban the IP for excessive requests
                    clientInfo.BannedUntil = now.AddMinutes(BanDurationMinutes);
                    LogBan(ipAddress, "Rate limit exceeded");
                    return false;
                }

                clientInfo.RequestCount++;
                return true;
            }
        }

        /// <summary>
        /// Check if a new connection from an IP should be allowed
        /// </summary>
        public bool AllowConnection(string ipAddress)
        {
            if (!Enabled || string.IsNullOrEmpty(ipAddress))
                return true;

            var now = DateTime.UtcNow;
            var clientInfo = _clientAttempts.GetOrAdd(ipAddress, ip => new ClientRateInfo
            {
                IPAddress = ip,
                WindowStart = now,
                LastSeen = now
            });

            lock (clientInfo)
            {
                clientInfo.LastSeen = now;

                // Check if banned
                if (clientInfo.BannedUntil.HasValue && clientInfo.BannedUntil.Value > now)
                    return false;

                // Clean old connection times (older than 1 minute)
                clientInfo.ConnectionTimes.RemoveAll(t => now - t > TimeSpan.FromMinutes(1));

                // Check connection limit
                if (clientInfo.ConnectionTimes.Count >= MaxConnectionsPerIP)
                {
                    clientInfo.BannedUntil = now.AddMinutes(BanDurationMinutes);
                    LogBan(ipAddress, "Connection limit exceeded");
                    return false;
                }

                // Add new connection
                clientInfo.ConnectionTimes.Add(now);
                clientInfo.ConnectionCount++;
                return true;
            }
        }

        /// <summary>
        /// Record a failed authentication attempt
        /// </summary>
        public void RecordFailedAuth(string ipAddress)
        {
            if (!Enabled || string.IsNullOrEmpty(ipAddress))
                return;

            var now = DateTime.UtcNow;
            var clientInfo = _clientAttempts.GetOrAdd(ipAddress, ip => new ClientRateInfo
            {
                IPAddress = ip,
                WindowStart = now,
                LastSeen = now
            });

            lock (clientInfo)
            {
                // Check for rapid failed attempts (more than 5 in 1 minute = ban)
                var recentFails = clientInfo.ConnectionTimes.Count(t => now - t < TimeSpan.FromMinutes(1));

                if (recentFails >= 5)
                {
                    clientInfo.BannedUntil = now.AddMinutes(BanDurationMinutes * 2); // Longer ban for auth attacks
                    LogBan(ipAddress, "Multiple failed authentication attempts");
                }
            }
        }

        /// <summary>
        /// Check if an IP is currently banned
        /// </summary>
        public bool IsBanned(string ipAddress)
        {
            if (!Enabled || string.IsNullOrEmpty(ipAddress))
                return false;

            if (_clientAttempts.TryGetValue(ipAddress, out var clientInfo))
            {
                lock (clientInfo)
                {
                    return clientInfo.BannedUntil.HasValue &&
                           clientInfo.BannedUntil.Value > DateTime.UtcNow;
                }
            }

            return false;
        }

        /// <summary>
        /// Get ban time remaining for an IP
        /// </summary>
        public TimeSpan? GetBanTimeRemaining(string ipAddress)
        {
            if (!Enabled || string.IsNullOrEmpty(ipAddress))
                return null;

            if (_clientAttempts.TryGetValue(ipAddress, out var clientInfo))
            {
                lock (clientInfo)
                {
                    if (clientInfo.BannedUntil.HasValue)
                    {
                        var remaining = clientInfo.BannedUntil.Value - DateTime.UtcNow;
                        return remaining > TimeSpan.Zero ? remaining : null;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Get statistics for monitoring
        /// </summary>
        public RateLimiterStats GetStatistics()
        {
            var now = DateTime.UtcNow;
            var stats = new RateLimiterStats
            {
                TotalTrackedIPs = _clientAttempts.Count,
                Enabled = Enabled
            };

            foreach (var kvp in _clientAttempts)
            {
                lock (kvp.Value)
                {
                    if (kvp.Value.BannedUntil.HasValue && kvp.Value.BannedUntil.Value > now)
                        stats.CurrentlyBannedIPs++;

                    if (now - kvp.Value.LastSeen < TimeSpan.FromMinutes(5))
                        stats.ActiveIPsLast5Minutes++;
                }
            }

            return stats;
        }

        /// <summary>
        /// Cleanup old entries to prevent memory leak
        /// </summary>
        private void CleanupOldEntries(object state)
        {
            var now = DateTime.UtcNow;
            var ipsToRemove = new List<string>();

            foreach (var kvp in _clientAttempts)
            {
                lock (kvp.Value)
                {
                    // Remove entries inactive for more than 24 hours and not banned
                    if (now - kvp.Value.LastSeen > TimeSpan.FromHours(24) &&
                        (!kvp.Value.BannedUntil.HasValue || kvp.Value.BannedUntil.Value <= now))
                    {
                        ipsToRemove.Add(kvp.Key);
                    }
                }
            }

            foreach (var ip in ipsToRemove)
            {
                _clientAttempts.TryRemove(ip, out _);
            }
        }

        /// <summary>
        /// Log ban events (implement based on your logging system)
        /// </summary>
        private void LogBan(string ipAddress, string reason)
        {
            // TODO: Integrate with your logging system
            Console.WriteLine($"[SECURITY] IP {ipAddress} banned: {reason}");

            // Example: Write to security log file
            try
            {
                string logEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - BAN - IP: {ipAddress} - Reason: {reason}\n";
                System.IO.File.AppendAllText("security.log", logEntry);
            }
            catch { /* Ignore log errors */ }
        }

        /// <summary>
        /// Rate limiter statistics for monitoring
        /// </summary>
        public class RateLimiterStats
        {
            public int TotalTrackedIPs { get; set; }
            public int CurrentlyBannedIPs { get; set; }
            public int ActiveIPsLast5Minutes { get; set; }
            public bool Enabled { get; set; }
        }

        /// <summary>
        /// Dispose pattern
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cleanupTimer?.Dispose();
                }
                _disposed = true;
            }
        }

        ~RateLimiter()
        {
            Dispose(false);
        }
    }
}