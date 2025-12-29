using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using VPN.Core.Enums;
using VPN.Core.Models;
using VPN.Core.Protocol;

namespace VPN.Server
{
    /// <summary>
    /// Forwards VPN traffic to the internet
    /// </summary>
    public class PacketForwarder : IDisposable
    {
        private bool _isRunning = false;
        private readonly SessionManager _sessionManager;

        // Statistics
        private long _totalBytesForwarded = 0;
        private int _totalPacketsForwarded = 0;

        public PacketForwarder(SessionManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        /// <summary>
        /// Start packet forwarding
        /// </summary>
        public void Start()
        {
            _isRunning = true;
            Log("Packet forwarder started");
        }

        /// <summary>
        /// Stop packet forwarding
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            Log("Packet forwarder stopped");
        }

        /// <summary>
        /// Forward decrypted data to the internet
        /// </summary>
        public async Task ForwardToInternet(string sessionId, byte[] decryptedData)
        {
            if (!_isRunning || decryptedData.Length == 0)
                return;

            try
            {
                var session = _sessionManager.GetSession(sessionId);
                if (session == null)
                {
                    Log($"Session not found: {sessionId}");
                    return;
                }

                // Update statistics
                session.BytesSent += decryptedData.Length;
                session.PacketsSent++;
                _totalBytesForwarded += decryptedData.Length;
                _totalPacketsForwarded++;

                // Simulate forwarding to internet
                // In a real VPN, this would actually send packets to their destination
                await SimulateInternetForwarding(decryptedData);

                Log($"Forwarded {decryptedData.Length} bytes for session {sessionId}");
            }
            catch (Exception ex)
            {
                Log($"Error forwarding packet: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulate receiving response from internet
        /// </summary>
        public async Task<byte[]> ReceiveFromInternet(string sessionId)
        {
            if (!_isRunning)
                return Array.Empty<byte>();

            try
            {
                var session = _sessionManager.GetSession(sessionId);
                if (session == null)
                    return Array.Empty<byte>();

                // Simulate receiving data from internet
                byte[] simulatedResponse = await SimulateInternetResponse();

                // Update statistics
                session.BytesReceived += simulatedResponse.Length;
                session.PacketsReceived++;

                return simulatedResponse;
            }
            catch (Exception ex)
            {
                Log($"Error receiving from internet: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Simulate internet forwarding (for demo purposes)
        /// </summary>
        private async Task SimulateInternetForwarding(byte[] data)
        {
            // In a real VPN, this would:
            // 1. Parse the IP packet
            // 2. Route it to the correct destination
            // 3. Send it via the server's network interface

            await Task.Delay(10); // Simulate network delay

            // Log first few bytes for debugging
            if (data.Length > 0)
            {
                string hex = BitConverter.ToString(data, 0, Math.Min(16, data.Length)).Replace("-", " ");
                Log($"Forwarding data (first 16 bytes): {hex}");
            }
        }

        /// <summary>
        /// Simulate internet response (for demo purposes)
        /// </summary>
        private async Task<byte[]> SimulateInternetResponse()
        {
            await Task.Delay(20); // Simulate network delay

            // Return simulated HTTP response
            string httpResponse = "HTTP/1.1 200 OK\r\n" +
                                 "Content-Type: text/html\r\n" +
                                 "Content-Length: 25\r\n\r\n" +
                                 "<h1>VPN Tunnel Working!</h1>";

            return Encoding.UTF8.GetBytes(httpResponse);
        }

        /// <summary>
        /// Get forwarding statistics
        /// </summary>
        public (long totalBytes, int totalPackets) GetStatistics()
        {
            return (_totalBytesForwarded, _totalPacketsForwarded);
        }

        /// <summary>
        /// Reset statistics
        /// </summary>
        public void ResetStatistics()
        {
            _totalBytesForwarded = 0;
            _totalPacketsForwarded = 0;
        }

        /// <summary>
        /// Check if forwarder is running
        /// </summary>
        public bool IsRunning => _isRunning;

        private void Log(string message)
        {
            Console.WriteLine($"[Forwarder] {DateTime.Now:HH:mm:ss} {message}");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}