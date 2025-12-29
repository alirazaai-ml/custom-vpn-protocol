using System;
using System.Collections.Generic;
using System.Text;

using VPN.Core.Enums;
using VPN.Core.Models;

namespace VPN.Core.Interfaces
{
    /// <summary>
    /// Interface for managing VPN sessions
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>
        /// Create new session
        /// </summary>
        Session CreateSession(string clientId, System.Net.IPEndPoint endpoint);

        /// <summary>
        /// Get session by ID
        /// </summary>
        Session GetSession(string sessionId);

        /// <summary>
        /// Update session status
        /// </summary>
        void UpdateSessionStatus(string sessionId, ConnectionStatus status);

        /// <summary>
        /// Update session activity
        /// </summary>
        void UpdateSessionActivity(string sessionId);

        /// <summary>
        /// Remove expired sessions
        /// </summary>
        List<Session> RemoveExpiredSessions(int timeoutSeconds = 300);

        /// <summary>
        /// Get all active sessions
        /// </summary>
        List<Session> GetAllSessions();

        /// <summary>
        /// Get session by client endpoint
        /// </summary>
        Session GetSessionByEndpoint(System.Net.IPEndPoint endpoint);

        /// <summary>
        /// Remove session
        /// </summary>
        bool RemoveSession(string sessionId);

        /// <summary>
        /// Get session statistics
        /// </summary>
        (int total, int active, int expired) GetSessionStatistics();
    }
}