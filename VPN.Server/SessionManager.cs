using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using VPN.Core.Interfaces;
using VPN.Core.Models;

namespace VPN.Server
{
    /// <summary>
    /// Manages VPN client sessions
    /// </summary>
    public class SessionManager : ISessionManager
    {
        private readonly ConcurrentDictionary<string, Session> _sessions = new();
        private readonly object _lock = new object();

        /// <summary>
        /// Create new session
        /// </summary>
        public Session CreateSession(string clientId, IPEndPoint endpoint)
        {
            var session = new Session
            {
                SessionId = Guid.NewGuid().ToString(),
                ClientId = clientId,
                ClientEndPoint = endpoint,
                Status = VPN.Core.Enums.ConnectionStatus.Connecting,
                ConnectedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow
            };

            _sessions[session.SessionId] = session;
            Log($"Session created: {session.SessionId} for {clientId} from {endpoint}");

            return session;
        }

        /// <summary>
        /// Get session by ID
        /// </summary>
        public Session GetSession(string sessionId)
        {
            _sessions.TryGetValue(sessionId, out var session);
            return session;
        }

        /// <summary>
        /// Update session status
        /// </summary>
        public void UpdateSessionStatus(string sessionId, VPN.Core.Enums.ConnectionStatus status)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Status = status;
                session.UpdateActivity();
                Log($"Session {sessionId} status updated to: {status}");
            }
        }

        /// <summary>
        /// Update session activity timestamp
        /// </summary>
        public void UpdateSessionActivity(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.UpdateActivity();
            }
        }

        /// <summary>
        /// Remove expired sessions
        /// </summary>
        public List<Session> RemoveExpiredSessions(int timeoutSeconds = 300)
        {
            var expired = new List<Session>();
            var now = DateTime.UtcNow;

            foreach (var session in _sessions.Values.ToList())
            {
                if (session.IsExpired(timeoutSeconds))
                {
                    if (_sessions.TryRemove(session.SessionId, out var removedSession))
                    {
                        expired.Add(removedSession);
                        Log($"Session expired: {session.SessionId} (Idle: {session.IdleTime.TotalSeconds:F0}s)");
                    }
                }
            }

            return expired;
        }

        /// <summary>
        /// Get all active sessions
        /// </summary>
        public List<Session> GetAllSessions()
        {
            return _sessions.Values.ToList();
        }

        /// <summary>
        /// Get session by client endpoint
        /// </summary>
        public Session GetSessionByEndpoint(IPEndPoint endpoint)
        {
            return _sessions.Values.FirstOrDefault(s =>
                s.ClientEndPoint.Address.Equals(endpoint.Address) &&
                s.ClientEndPoint.Port == endpoint.Port);
        }

        /// <summary>
        /// Remove session
        /// </summary>
        public bool RemoveSession(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                Log($"Session removed: {sessionId}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get session statistics
        /// </summary>
        public (int total, int active, int expired) GetSessionStatistics()
        {
            var now = DateTime.UtcNow;
            var sessions = _sessions.Values.ToList();

            int total = sessions.Count;
            int active = sessions.Count(s => !s.IsExpired());
            int expired = total - active;

            return (total, active, expired);
        }

        /// <summary>
        /// Update session encryption key
        /// </summary>
        public void UpdateSessionKey(string sessionId, byte[] sessionKey, byte[] iv)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.SessionKey = sessionKey;
                session.Iv = iv;
                session.UpdateActivity();
                Log($"Session key updated for: {sessionId}");
            }
        }

        /// <summary>
        /// Get active session count
        /// </summary>
        public int GetActiveSessionCount()
        {
            return _sessions.Count(s => !s.Value.IsExpired());
        }

        /// <summary>
        /// Cleanup all sessions
        /// </summary>
        public void CleanupAllSessions()
        {
            _sessions.Clear();
            Log("All sessions cleaned up");
        }

        private void Log(string message)
        {
            Console.WriteLine($"[SessionManager] {DateTime.Now:HH:mm:ss} {message}");
        }
    }
}