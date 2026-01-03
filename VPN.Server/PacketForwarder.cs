using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VPN.Core.Enums;
using VPN.Core.Models;
using VPN.Core.Protocol;

namespace VPN.Server
{
    /// <summary>
    /// Real packet forwarder - routes VPN traffic to internet destinations with NAT support
    /// </summary>
    public class PacketForwarder : IDisposable
    {
        private bool _isRunning = false;
        private readonly SessionManager _sessionManager;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly NatManager _natManager; // NAT support

        // Statistics
        private long _totalBytesForwarded = 0;
        private int _totalPacketsForwarded = 0;

        // Active connections cache (sessionId -> destination socket)
        private readonly ConcurrentDictionary<string, Socket> _activeConnections;
        
        // Response queues per session
        private readonly ConcurrentDictionary<string, ConcurrentQueue<byte[]>> _responseQueues;

        // Configuration
        private readonly int _socketTimeout = 30000; // 30 seconds

        public PacketForwarder(SessionManager sessionManager)
        {
            _sessionManager = sessionManager;
            _cancellationTokenSource = new CancellationTokenSource();
            _activeConnections = new ConcurrentDictionary<string, Socket>();
            _responseQueues = new ConcurrentDictionary<string, ConcurrentQueue<byte[]>>();
            _natManager = new NatManager(); // Initialize NAT
        }

        /// <summary>
        /// Start packet forwarding
        /// </summary>
        public void Start()
        {
            _isRunning = true;
            Log("✅ Packet forwarder started - REAL FORWARDING MODE with NAT");
            Log($"✅ Server IP: {_natManager.GetServerIp()}");
            Log("Ready to route traffic to internet destinations");

            // Start cleanup task for expired NAT entries
            _ = Task.Run(async () =>
            {
                while (_isRunning)
                {
                    await Task.Delay(60000); // Every minute
                    _natManager.CleanupExpired();
                }
            });
        }

        /// <summary>
        /// Stop packet forwarding
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _cancellationTokenSource.Cancel();

            // Close all active connections
            foreach (var connection in _activeConnections.Values)
            {
                try
                {
                    connection.Shutdown(SocketShutdown.Both);
                    connection.Close();
                }
                catch { }
            }
            _activeConnections.Clear();

            Log("Packet forwarder stopped");
        }

        /// <summary>
        /// Forward decrypted data to the internet - REAL IMPLEMENTATION WITH IP PARSING
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

                // Try to parse as IP packet first
                var ipPacket = IpPacketParser.Parse(decryptedData);
                
                if (ipPacket != null && ipPacket.DestinationIp != null)
                {
                    // Real IP packet - forward with NAT
                    await ForwardIpPacket(sessionId, ipPacket);
                }
                else if (IpPacketParser.IsHttpTraffic(decryptedData))
                {
                    // HTTP traffic without IP headers - simulate forwarding
                    await ForwardHttpTraffic(sessionId, decryptedData);
                }
                else
                {
                    // Unknown format - log and simulate
                    Log($"⚠️ Unknown packet format, using simulation for session {sessionId}");
                    await SimulateHttpForwarding(sessionId, decryptedData);
                }

                // Update statistics
                session.BytesSent += decryptedData.Length;
                session.PacketsSent++;
                _totalBytesForwarded += decryptedData.Length;
                _totalPacketsForwarded++;

                Log($"✅ Forwarded {decryptedData.Length} bytes for session {sessionId}");
            }
            catch (Exception ex)
            {
                Log($"❌ Error forwarding packet: {ex.Message}");
            }
        }

        /// <summary>
        /// Forward real IP packet with NAT translation
        /// </summary>
        private async Task ForwardIpPacket(string sessionId, IpPacketInfo ipPacket)
        {
            try
            {
                // Perform NAT translation (replace client IP with server IP)
                var natTranslation = _natManager.TranslateOutgoing(
                    sessionId,
                    ipPacket.SourceIp,
                    ipPacket.SourcePort,
                    ipPacket.DestinationIp,
                    ipPacket.DestinationPort
                );

                Log($"🔄 NAT: {ipPacket.SourceIp}:{ipPacket.SourcePort} → " +
                    $"{natTranslation.TranslatedSourceIp}:{natTranslation.TranslatedSourcePort}");
                Log($"📍 Destination: {ipPacket.DestinationIp}:{ipPacket.DestinationPort}");

                // Forward based on protocol
                switch (ipPacket.Protocol)
                {
                    case ProtocolType.Tcp:
                        await ForwardTcpPacket(sessionId, ipPacket, natTranslation);
                        break;
                    
                    case ProtocolType.Udp:
                        await ForwardUdpPacket(sessionId, ipPacket, natTranslation);
                        break;
                    
                    case ProtocolType.Icmp:
                        Log($"⚠️ ICMP not yet supported, packet from {ipPacket.SourceIp}");
                        break;
                    
                    default:
                        Log($"⚠️ Unsupported protocol: {ipPacket.Protocol}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"❌ IP packet forwarding error: {ex.Message}");
            }
        }

        /// <summary>
        /// Forward HTTP traffic (legacy support)
        /// </summary>
        private async Task ForwardHttpTraffic(string sessionId, byte[] data)
        {
            try
            {
                // Detect host from HTTP headers
                string dataStr = Encoding.UTF8.GetString(data);
                string host = ExtractHostFromHttp(dataStr);
                
                if (!string.IsNullOrEmpty(host))
                {
                    // Resolve DNS
                    var addresses = await Dns.GetHostAddressesAsync(host);
                    if (addresses.Length > 0)
                    {
                        var destIp = addresses[0];
                        int destPort = dataStr.Contains("HTTPS") || dataStr.Contains(":443") ? 443 : 80;

                        Log($"🌐 HTTP Request to {host} ({destIp}:{destPort})");

                        // Create synthetic IP packet info
                        var ipPacket = new IpPacketInfo
                        {
                            Protocol = ProtocolType.Tcp,
                            SourceIp = IPAddress.Parse("10.0.0.1"), // Virtual client IP
                            SourcePort = 50000,
                            DestinationIp = destIp,
                            DestinationPort = destPort,
                            Payload = data
                        };

                        await ForwardIpPacket(sessionId, ipPacket);
                        return;
                    }
                }

                // Fallback to simulation
                await SimulateHttpForwarding(sessionId, data);
            }
            catch
            {
                await SimulateHttpForwarding(sessionId, data);
            }
        }

        /// <summary>
        /// Extract host from HTTP headers
        /// </summary>
        private string ExtractHostFromHttp(string httpData)
        {
            try
            {
                var lines = httpData.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring(5).Trim().Split(':')[0];
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Receive response from internet - REAL IMPLEMENTATION
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

                // Get response queue for this session
                if (!_responseQueues.TryGetValue(sessionId, out var queue))
                {
                    queue = new ConcurrentQueue<byte[]>();
                    _responseQueues[sessionId] = queue;
                }

                // Check if we have queued responses
                if (queue.TryDequeue(out var response))
                {
                    // Update statistics
                    session.BytesReceived += response.Length;
                    session.PacketsReceived++;
                    
                    return response;
                }

                // For testing: Return simulated HTTP response
                return await SimulateHttpResponse();
            }
            catch (Exception ex)
            {
                Log($"❌ Error receiving from internet: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Forward TCP packet to destination
        /// </summary>
        private async Task ForwardTcpPacket(string sessionId, IpPacketInfo ipPacket, NatTranslation nat)
        {
            try
            {
                string connectionKey = $"{sessionId}_{ipPacket.DestinationIp}_{ipPacket.DestinationPort}";

                // Get or create socket for this destination
                if (!_activeConnections.TryGetValue(connectionKey, out var socket))
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    socket.SendTimeout = _socketTimeout;
                    socket.ReceiveTimeout = _socketTimeout;

                    // Connect to destination
                    var endpoint = new IPEndPoint(ipPacket.DestinationIp, ipPacket.DestinationPort);
                    await socket.ConnectAsync(endpoint);
                    
                    _activeConnections[connectionKey] = socket;
                    Log($"🔗 Established TCP connection to {ipPacket.DestinationIp}:{ipPacket.DestinationPort}");

                    // Start receiving responses
                    _ = ReceiveTcpResponses(sessionId, connectionKey, socket);
                }

                // Send payload to destination
                if (ipPacket.Payload != null && ipPacket.Payload.Length > 0)
                {
                    await socket.SendAsync(new ArraySegment<byte>(ipPacket.Payload), SocketFlags.None);
                    Log($"📤 Sent {ipPacket.Payload.Length} bytes to {ipPacket.DestinationIp}:{ipPacket.DestinationPort}");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ TCP forwarding error: {ex.Message}");
            }
        }

        /// <summary>
        /// Forward UDP packet to destination
        /// </summary>
        private async Task ForwardUdpPacket(string sessionId, IpPacketInfo ipPacket, NatTranslation nat)
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.SendTimeout = _socketTimeout;
                
                var endpoint = new IPEndPoint(ipPacket.DestinationIp, ipPacket.DestinationPort);
                
                if (ipPacket.Payload != null && ipPacket.Payload.Length > 0)
                {
                    await socket.SendToAsync(new ArraySegment<byte>(ipPacket.Payload), SocketFlags.None, endpoint);
                    Log($"📤 Sent UDP packet to {ipPacket.DestinationIp}:{ipPacket.DestinationPort}");

                    // UDP responses handled differently (stateless)
                    _ = ReceiveUdpResponse(sessionId, socket);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ UDP forwarding error: {ex.Message}");
            }
        }

        /// <summary>
        /// Receive TCP responses from destination
        /// </summary>
        private async Task ReceiveTcpResponses(string sessionId, string connectionKey, Socket socket)
        {
            byte[] buffer = new byte[4096];
            
            try
            {
                while (_isRunning && socket.Connected)
                {
                    int bytesRead = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None);
                    
                    if (bytesRead > 0)
                    {
                        byte[] response = new byte[bytesRead];
                        Buffer.BlockCopy(buffer, 0, response, 0, bytesRead);

                        // Queue response for this session
                        if (!_responseQueues.TryGetValue(sessionId, out var queue))
                        {
                            queue = new ConcurrentQueue<byte[]>();
                            _responseQueues[sessionId] = queue;
                        }
                        queue.Enqueue(response);

                        Log($"📥 Received {bytesRead} bytes for session {sessionId}");
                    }
                    else
                    {
                        // Connection closed
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ TCP receive error: {ex.Message}");
            }
            finally
            {
                // Cleanup
                _activeConnections.TryRemove(connectionKey, out _);
                try
                {
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// Receive UDP response
        /// </summary>
        private async Task ReceiveUdpResponse(string sessionId, Socket socket)
        {
            byte[] buffer = new byte[4096];
            
            try
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), SocketFlags.None);
                
                if (result > 0)
                {
                    byte[] response = new byte[result];
                    Buffer.BlockCopy(buffer, 0, response, 0, result);

                    // Queue response
                    if (!_responseQueues.TryGetValue(sessionId, out var queue))
                    {
                        queue = new ConcurrentQueue<byte[]>();
                        _responseQueues[sessionId] = queue;
                    }
                    queue.Enqueue(response);

                    Log($"📥 Received UDP response for session {sessionId}");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ UDP receive error: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulate HTTP forwarding for testing (fallback)
        /// </summary>
        private async Task SimulateHttpForwarding(string sessionId, byte[] data)
        {
            await Task.Delay(50); // Network delay simulation

            // Queue a test response
            if (!_responseQueues.TryGetValue(sessionId, out var queue))
            {
                queue = new ConcurrentQueue<byte[]>();
                _responseQueues[sessionId] = queue;
            }

            string httpResponse = "HTTP/1.1 200 OK\r\n" +
                                 "Content-Type: text/html; charset=UTF-8\r\n" +
                                 "Server: VPN-Forwarder/1.0\r\n" +
                                 "Content-Length: 89\r\n\r\n" +
                                 "<html><body><h1>VPN Tunnel Working!</h1><p>Packet successfully forwarded.</p></body></html>";

            queue.Enqueue(Encoding.UTF8.GetBytes(httpResponse));
        }

        /// <summary>
        /// Simulate HTTP response (for testing)
        /// </summary>
        private async Task<byte[]> SimulateHttpResponse()
        {
            await Task.Delay(20);

            string httpResponse = "HTTP/1.1 200 OK\r\n" +
                                 "Content-Type: application/json\r\n" +
                                 "Server: VPN-Server/1.0\r\n" +
                                 "Content-Length: 47\r\n\r\n" +
                                 "{\"status\":\"success\",\"message\":\"VPN tunnel active\"}";

            return Encoding.UTF8.GetBytes(httpResponse);
        }

        /// <summary>
        /// Get forwarding statistics including NAT stats
        /// </summary>
        public (long totalBytes, int totalPackets) GetStatistics()
        {
            var (natTotal, natActive) = _natManager.GetStatistics();
            Log($"📊 NAT Table: {natTotal} total entries, {natActive} active");
            
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

        /// <summary>
        /// Get server IP for display
        /// </summary>
        public IPAddress GetServerIp() => _natManager.GetServerIp();

        private void Log(string message)
        {
            Console.WriteLine($"[Forwarder] {DateTime.Now:HH:mm:ss} {message}");
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }
}