using System;
using System.Collections.Generic;
using System.Text;

namespace VPN.Core.Interfaces
{
    /// <summary>
    /// Interface for managing VPN tunnel
    /// </summary>
    public interface ITunnelManager
    {
        /// <summary>
        /// Start tunnel
        /// </summary>
        void StartTunnel();

        /// <summary>
        /// Stop tunnel
        /// </summary>
        void StopTunnel();

        /// <summary>
        /// Check if tunnel is running
        /// </summary>
        bool IsTunnelRunning();

        /// <summary>
        /// Get tunnel statistics
        /// </summary>
        (long bytesIn, long bytesOut, int packetsIn, int packetsOut) GetTunnelStatistics();

        /// <summary>
        /// Process incoming tunnel data
        /// </summary>
        void ProcessIncomingData(byte[] data);

        /// <summary>
        /// Send data through tunnel
        /// </summary>
        void SendData(byte[] data);
    }
}