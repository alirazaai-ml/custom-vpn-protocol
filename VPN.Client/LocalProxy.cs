using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VPN.Client
{
    /// <summary>
    /// SOCKS5 proxy server with COMPLETE DNS tunneling for VPN client
    /// </summary>
    public class LocalProxy : IDisposable
    {
        private readonly TunnelManager _tunnelManager;
        private readonly int _proxyPort;
        private TcpListener _tcpListener;
        private UdpClient _dnsListener; // DNS proxy
        private bool _isRunning = false;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentDictionary<string, ProxyConnection> _connections;
        private readonly int _dnsPort = 5353; // Local DNS port (non-privileged)

        // ✅ NEW: Complete DNS tunneling support
        private readonly ConcurrentDictionary<ushort, DnsQueryContext> _pendingDnsQueries = new();
        private readonly ConcurrentQueue<DnsResponse> _dnsResponseQueue = new();

        public LocalProxy(TunnelManager tunnelManager, int proxyPort = 1080)
        {
            _tunnelManager = tunnelManager;
            _proxyPort = proxyPort;
            _cancellationTokenSource = new CancellationTokenSource();
            _connections = new ConcurrentDictionary<string, ProxyConnection>();

            // ✅ Subscribe to DNS responses from tunnel
            _tunnelManager.DnsResponseReceived += OnDnsResponseFromTunnel;
        }

        /// <summary>
        /// Start SOCKS5 proxy and DNS proxy servers
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                Log("Proxy already running");
                return;
            }

            try
            {
                _isRunning = true;

                // Start SOCKS5 TCP listener on proxy port
                _tcpListener = new TcpListener(IPAddress.Loopback, _proxyPort);
                _tcpListener.Start();
                Log($"✅ SOCKS5 proxy started on port {_proxyPort}");

                // Start DNS UDP listener (use non-privileged port 5353)
                try
                {
                    _dnsListener = new UdpClient(_dnsPort);
                    Log($"✅ DNS proxy started on port {_dnsPort}");
                    Log($"📝 Set Windows DNS to 127.0.0.1:{_dnsPort} to use VPN DNS tunneling");

                    // Start DNS accept loop
                    Task.Run(() => DnsProxyLoop());
                    
                    // Start DNS cleanup task
                    Task.Run(async () =>
                    {
                        while (_isRunning)
                        {
                            await Task.Delay(30000); // Cleanup every 30 seconds
                            if (_isRunning)
                            {
                                CleanupExpiredDnsQueries();
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log($"⚠️ Could not start DNS proxy on port {_dnsPort}: {ex.Message}");
                    Log("DNS queries will not be tunneled (requires admin rights for port 53)");
                }

                // Start SOCKS5 accept loop
                Task.Run(() => AcceptClientsLoop());
            }
            catch (Exception ex)
            {
                Log($"❌ Error starting proxy: {ex.Message}");
                _isRunning = false;
            }
        }

        /// <summary>
        /// Stop SOCKS5 proxy and DNS proxy servers
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            try
            {
                _isRunning = false;
                _cancellationTokenSource.Cancel();

                // Close all active connections
                foreach (var connection in _connections.Values)
                {
                    connection.Dispose();
                }
                _connections.Clear();

                _tcpListener?.Stop();
                _dnsListener?.Close();

                Log("SOCKS5 and DNS proxy stopped");
            }
            catch (Exception ex)
            {
                Log($"Error stopping proxy: {ex.Message}");
            }
        }

        /// <summary>
        /// DNS proxy loop - ✅ COMPLETE DNS tunneling through VPN
        /// </summary>
        private async Task DnsProxyLoop()
        {
            try
            {
                Log("🔍 DNS proxy loop started - tunneling DNS through VPN");

                while (_isRunning)
                {
                    try
                    {
                        var result = await _dnsListener.ReceiveAsync();
                        byte[] dnsQuery = result.Buffer;
                        IPEndPoint clientEndpoint = result.RemoteEndPoint;

                        // ✅ Extract DNS query ID for tracking
                        ushort queryId = ExtractDnsQueryId(dnsQuery);

                        Log($"🔍 DNS query received from {clientEndpoint.Address} (Query ID: {queryId})");

                        // ✅ Store query context for response routing
                        var context = new DnsQueryContext
                        {
                            QueryId = queryId,
                            ClientEndpoint = clientEndpoint,
                            OriginalQuery = dnsQuery,
                            Timestamp = DateTime.Now
                        };

                        _pendingDnsQueries.TryAdd(queryId, context);

                        // ✅ Send DNS query through VPN tunnel with proper identification
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                // Forward DNS query through tunnel with query ID
                                await _tunnelManager.SendDnsQueryAsync(dnsQuery, queryId);
                                Log($"📤 DNS query {queryId} sent through VPN tunnel");

                                // ✅ Wait for DNS response with timeout
                                byte[] dnsResponse = await WaitForDnsResponse(queryId, 10000); // 10 second timeout

                                if (dnsResponse != null && dnsResponse.Length > 0)
                                {
                                    // Send response back to client
                                    await _dnsListener.SendAsync(dnsResponse, dnsResponse.Length, clientEndpoint);
                                    Log($"✅ DNS response {queryId} sent back to {clientEndpoint.Address} ({dnsResponse.Length} bytes)");
                                }
                                else
                                {
                                    Log($"⏰ DNS query {queryId} timeout - no response from VPN server");

                                    // ✅ Send error response to client
                                    byte[] errorResponse = CreateDnsErrorResponse(dnsQuery);
                                    if (errorResponse != null)
                                    {
                                        await _dnsListener.SendAsync(errorResponse, errorResponse.Length, clientEndpoint);
                                        Log($"📤 DNS error response sent for query {queryId}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"❌ DNS forwarding error for query {queryId}: {ex.Message}");
                            }
                            finally
                            {
                                // Clean up context
                                _pendingDnsQueries.TryRemove(queryId, out _);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        if (_isRunning)
                        {
                            Log($"❌ DNS receive error: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    Log($"❌ DNS proxy loop error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// ✅ COMPLETE: Wait for DNS response from tunnel
        /// </summary>
        private async Task<byte[]> WaitForDnsResponse(ushort queryId, int timeoutMs)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromMilliseconds(timeoutMs);

            while ((DateTime.Now - startTime) < timeout)
            {
                // Check for response in queue
                if (_dnsResponseQueue.TryDequeue(out var response) && response.QueryId == queryId)
                {
                    Log($"✅ Found DNS response for query {queryId} ({response.Data.Length} bytes)");
                    return response.Data;
                }

                // Short delay to prevent CPU spinning
                await Task.Delay(50);
            }

            Log($"⏰ DNS response timeout for query {queryId} after {timeoutMs}ms");
            return null;
        }

        /// <summary>
        /// ✅ NEW: Handle DNS response received from VPN tunnel
        /// </summary>
        private void OnDnsResponseFromTunnel(object sender, DnsResponseEventArgs e)
        {
            try
            {
                Log($"📥 DNS response received from tunnel (Query ID: {e.QueryId}, {e.ResponseData.Length} bytes)");

                // Queue the response for the waiting query
                _dnsResponseQueue.Enqueue(new DnsResponse
                {
                    QueryId = e.QueryId,
                    Data = e.ResponseData,
                    Timestamp = DateTime.Now
                });

                Log($"✅ DNS response {e.QueryId} queued for delivery");
            }
            catch (Exception ex)
            {
                Log($"❌ Error handling DNS response: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NEW: Extract DNS query ID from DNS packet
        /// </summary>
        private ushort ExtractDnsQueryId(byte[] dnsPacket)
        {
            try
            {
                if (dnsPacket != null && dnsPacket.Length >= 2)
                {
                    // DNS query ID is in the first 2 bytes (big-endian)
                    return (ushort)((dnsPacket[0] << 8) | dnsPacket[1]);
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error extracting DNS query ID: {ex.Message}");
            }

            // Fallback to random ID
            return (ushort)new Random().Next(1, 65535);
        }

        /// <summary>
        /// ✅ NEW: Create DNS error response
        /// </summary>
        private byte[] CreateDnsErrorResponse(byte[] originalQuery)
        {
            try
            {
                if (originalQuery == null || originalQuery.Length < 12)
                    return null;

                // Create a basic DNS error response (SERVFAIL)
                byte[] response = new byte[originalQuery.Length];
                Array.Copy(originalQuery, response, originalQuery.Length);

                // Set response flag and error code
                response[2] = 0x81; // QR=1 (response), OPCODE=0, AA=0, TC=0, RD=1
                response[3] = 0x02; // RA=0, Z=0, RCODE=2 (SERVFAIL)

                // Clear answer, authority, and additional counts
                response[6] = 0x00; // ANCOUNT high
                response[7] = 0x00; // ANCOUNT low
                response[8] = 0x00; // NSCOUNT high
                response[9] = 0x00; // NSCOUNT low
                response[10] = 0x00; // ARCOUNT high
                response[11] = 0x00; // ARCOUNT low

                return response;
            }
            catch (Exception ex)
            {
                Log($"❌ Error creating DNS error response: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ✅ NEW: Clean up expired DNS queries
        /// </summary>
        private void CleanupExpiredDnsQueries()
        {
            try
            {
                var cutoff = DateTime.Now.AddMinutes(-5);
                var expired = new List<ushort>();

                foreach (var kvp in _pendingDnsQueries)
                {
                    if (kvp.Value.Timestamp < cutoff)
                    {
                        expired.Add(kvp.Key);
                    }
                }

                foreach (var queryId in expired)
                {
                    if (_pendingDnsQueries.TryRemove(queryId, out var context))
                    {
                        Log($"🧹 Cleaned up expired DNS query {queryId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error cleaning up DNS queries: {ex.Message}");
            }
        }

        /// <summary>
        /// Accept clients loop
        /// </summary>
        private async Task AcceptClientsLoop()
        {
            try
            {
                while (_isRunning)
                {
                    if (_tcpListener.Pending())
                    {
                        var client = await _tcpListener.AcceptTcpClientAsync();
                        _ = HandleClientAsync(client); // Fire and forget
                    }
                    else
                    {
                        await Task.Delay(10);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    Log($"Accept loop error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handle SOCKS5 client connection
        /// </summary>
        private async Task HandleClientAsync(TcpClient client)
        {
            string connectionId = Guid.NewGuid().ToString().Substring(0, 8);
            ProxyConnection connection = null;

            // ✅ Get client endpoint for logging
            var clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

            try
            {
                // ✅ CHECK: Ensure tunnel is active before accepting connections
                if (!_tunnelManager.IsTunnelActive)
                {
                    Log($"❌ Rejecting connection {connectionId} - VPN tunnel not active");
                    client.Close();
                    return;
                }

                connection = new ProxyConnection(connectionId, client, _tunnelManager);
                _connections[connectionId] = connection;

                Log($"🔗 New SOCKS5 connection: {connectionId} from {clientEndpoint}");

                // ✅ Set timeout for client operations
                client.ReceiveTimeout = 30000; // 30 seconds
                client.SendTimeout = 30000;

                // SOCKS5 handshake
                if (!await PerformSocks5Handshake(connection))
                {
                    Log($"❌ SOCKS5 handshake failed for {connectionId}");
                    // ✅ Send error response to browser
                    await SendSocks5Error(connection, 0x01); // General SOCKS server failure
                    return;
                }

                Log($"✅ {connectionId}: SOCKS5 handshake completed");

                // SOCKS5 request
                if (!await HandleSocks5Request(connection))
                {
                    Log($"❌ SOCKS5 request failed for {connectionId}");
                    // ✅ Send error response to browser  
                    await SendSocks5Error(connection, 0x07); // Command not supported
                    return;
                }

                Log($"✅ {connectionId}: SOCKS5 request processed - {connection.DestinationHost}:{connection.DestinationPort}");

                // Forward data
                await ForwardDataAsync(connection);
            }
            catch (Exception ex)
            {
                Log($"❌ Error handling client {connectionId}: {ex.Message}");

                // ✅ Try to send error response to browser if possible
                try
                {
                    if (connection?.Stream != null && client.Connected)
                    {
                        await SendSocks5Error(connection, 0x01); // General failure
                    }
                }
                catch
                {
                    // Ignore errors when sending error response
                }
            }
            finally
            {
                _connections.TryRemove(connectionId, out _);
                connection?.Dispose();
                Log($"🔌 SOCKS5 connection closed: {connectionId}");
            }
        }

        /// <summary>
        /// ✅ NEW: Send SOCKS5 error response to browser
        /// </summary>
        private async Task SendSocks5Error(ProxyConnection connection, byte errorCode)
        {
            try
            {
                byte[] errorResponse = {
                    0x05, // SOCKS version
                    errorCode, // Error code
                    0x00, // Reserved
                    0x01, // IPv4
                    0x00, 0x00, 0x00, 0x00, // Address (0.0.0.0)
                    0x00, 0x00 // Port (0)
                };

                await connection.Stream.WriteAsync(errorResponse, 0, errorResponse.Length);
                await connection.Stream.FlushAsync();

                Log($"📤 {connection.Id}: Sent SOCKS5 error response (code: 0x{errorCode:X2})");
            }
            catch (Exception ex)
            {
                Log($"❌ Error sending SOCKS5 error response: {ex.Message}");
            }
        }

        /// <summary>
        /// Perform SOCKS5 handshake
        /// </summary>
        private async Task<bool> PerformSocks5Handshake(ProxyConnection connection)
        {
            try
            {
                // ✅ Set short timeout for handshake
                var originalTimeout = connection.Client.ReceiveTimeout;
                connection.Client.ReceiveTimeout = 5000; // 5 seconds for handshake

                // Read greeting
                byte[] greeting = new byte[2];
                int bytesRead = await connection.Stream.ReadAsync(greeting, 0, 2);

                if (bytesRead != 2)
                {
                    Log($"❌ {connection.Id}: Invalid greeting length: {bytesRead}");
                    return false;
                }

                if (greeting[0] != 0x05) // SOCKS version 5
                {
                    Log($"❌ {connection.Id}: Invalid SOCKS version: {greeting[0]}");
                    return false;
                }

                int methodCount = greeting[1];
                if (methodCount == 0 || methodCount > 255)
                {
                    Log($"❌ {connection.Id}: Invalid method count: {methodCount}");
                    return false;
                }

                byte[] methods = new byte[methodCount];
                int methodsRead = await connection.Stream.ReadAsync(methods, 0, methodCount);

                if (methodsRead != methodCount)
                {
                    Log($"❌ {connection.Id}: Incomplete methods read: {methodsRead}/{methodCount}");
                    return false;
                }

                // ✅ Check if no authentication method is supported
                bool noAuthSupported = false;
                for (int i = 0; i < methodCount; i++)
                {
                    if (methods[i] == 0x00) // No authentication
                    {
                        noAuthSupported = true;
                        break;
                    }
                }

                if (!noAuthSupported)
                {
                    Log($"❌ {connection.Id}: No authentication method not supported by client");
                    // Send "no acceptable methods" response
                    byte[] noMethodsResponse = { 0x05, 0xFF };
                    await connection.Stream.WriteAsync(noMethodsResponse, 0, 2);
                    return false;
                }

                // Send response: No authentication required (0x00)
                byte[] response = { 0x05, 0x00 };
                await connection.Stream.WriteAsync(response, 0, 2);
                await connection.Stream.FlushAsync(); // ✅ Ensure data is sent immediately

                // ✅ Restore original timeout
                connection.Client.ReceiveTimeout = originalTimeout;

                Log($"✅ {connection.Id}: SOCKS5 handshake successful");
                return true;
            }
            catch (Exception ex)
            {
                Log($"❌ {connection.Id}: Handshake error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Handle SOCKS5 connection request with HTTPS support
        /// </summary>
        private async Task<bool> HandleSocks5Request(ProxyConnection connection)
        {
            try
            {
                // Read request header
                byte[] request = new byte[4];
                int bytesRead = await connection.Stream.ReadAsync(request, 0, 4);

                if (bytesRead != 4 || request[0] != 0x05) // SOCKS5
                    return false;

                byte command = request[1];
                byte addressType = request[3];

                // Parse destination address
                string destHost = "";
                int destPort = 0;

                if (addressType == 0x01) // IPv4
                {
                    byte[] ipBytes = new byte[4];
                    await connection.Stream.ReadAsync(ipBytes, 0, 4);
                    destHost = new IPAddress(ipBytes).ToString();
                }
                else if (addressType == 0x03) // Domain name
                {
                    byte[] lengthByte = new byte[1];
                    await connection.Stream.ReadAsync(lengthByte, 0, 1);
                    byte[] domainBytes = new byte[lengthByte[0]];
                    await connection.Stream.ReadAsync(domainBytes, 0, domainBytes.Length);
                    destHost = Encoding.ASCII.GetString(domainBytes);
                }
                else if (addressType == 0x04) // IPv6
                {
                    byte[] ipBytes = new byte[16];
                    await connection.Stream.ReadAsync(ipBytes, 0, 16);
                    destHost = new IPAddress(ipBytes).ToString();
                }
                else
                {
                    Log($"❌ {connection.Id}: Unsupported address type: 0x{addressType:X2}");
                    return false;
                }

                // Read port
                byte[] portBytes = new byte[2];
                await connection.Stream.ReadAsync(portBytes, 0, 2);
                destPort = (portBytes[0] << 8) | portBytes[1];

                connection.DestinationHost = destHost;
                connection.DestinationPort = destPort;

                // ✅ FIX: Handle CONNECT command (for HTTPS)
                if (command == 0x01) // CONNECT command
                {
                    Log($"📍 {connection.Id}: HTTPS CONNECT to {destHost}:{destPort}");

                    // ✅ Send immediate success response for CONNECT
                    byte[] response = {
                0x05, // SOCKS version
                0x00, // Success
                0x00, // Reserved
                0x01, // IPv4
                0x00, 0x00, 0x00, 0x00, // Bind address (0.0.0.0)
                0x00, 0x00 // Bind port (0)
            };
                    await connection.Stream.WriteAsync(response, 0, response.Length);
                    await connection.Stream.FlushAsync();

                    Log($"✅ {connection.Id}: HTTPS CONNECT response sent");

                    return true;
                }
                else if (command == 0x02) // BIND command
                {
                    Log($"❌ {connection.Id}: BIND command not supported");
                    return false;
                }
                else if (command == 0x03) // UDP ASSOCIATE
                {
                    Log($"❌ {connection.Id}: UDP ASSOCIATE not supported");
                    return false;
                }
                else
                {
                    Log($"❌ {connection.Id}: Unknown command: 0x{command:X2}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"❌ {connection.Id}: SOCKS5 request error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Forward data between client and VPN tunnel - Optimized for HTTPS
        /// </summary>
        private async Task ForwardDataAsync(ProxyConnection connection)
        {
            try
            {
                Log($"🚀 {connection.Id}: Starting HTTPS tunneling for {connection.DestinationHost}:{connection.DestinationPort}");

                // ✅ Create a connection context for the VPN server
                // For HTTPS, we need to send the CONNECT request through tunnel

                // Send initial CONNECT request through tunnel
                string connectRequest = $"CONNECT {connection.DestinationHost}:{connection.DestinationPort} HTTP/1.1\r\n" +
                                       $"Host: {connection.DestinationHost}:{connection.DestinationPort}\r\n" +
                                       $"Proxy-Connection: keep-alive\r\n" +
                                       $"\r\n";

                byte[] connectBytes = Encoding.UTF8.GetBytes(connectRequest);
                _tunnelManager.SendDataWithContext(connection.Id, connectBytes);

                Log($"📤 {connection.Id}: Sent CONNECT request through tunnel");

                // ✅ Bidirectional data forwarding
                var clientStream = connection.Stream;
                byte[] clientBuffer = new byte[8192];
                byte[] tunnelBuffer = new byte[8192];

                var cts = new CancellationTokenSource();
                var ct = cts.Token;

                // ✅ Task 1: Read from browser and send through VPN
                var clientToTunnelTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!ct.IsCancellationRequested && connection.Client.Connected)
                        {
                            if (clientStream.DataAvailable)
                            {
                                int bytesRead = await clientStream.ReadAsync(clientBuffer, 0, clientBuffer.Length, ct);
                                if (bytesRead > 0)
                                {
                                    byte[] data = new byte[bytesRead];
                                    Buffer.BlockCopy(clientBuffer, 0, data, 0, bytesRead);

                                    _tunnelManager.SendDataWithContext(connection.Id, data);
                                    connection.BytesSent += bytesRead;

                                    Log($"📤 {connection.Id}: Browser → Tunnel: {bytesRead} bytes");
                                }
                            }
                            else
                            {
                                await Task.Delay(10, ct);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Normal cancellation
                    }
                    catch (Exception ex)
                    {
                        Log($"❌ {connection.Id}: Browser→Tunnel error: {ex.Message}");
                    }
                });

                // ✅ Task 2: Read from VPN and send to browser
                var tunnelToClientTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!ct.IsCancellationRequested && connection.Client.Connected)
                        {
                            byte[] responseData = await _tunnelManager.ReceiveDataForConnection(connection.Id, timeoutMs: 50);

                            if (responseData != null && responseData.Length > 0)
                            {
                                // ✅ CRITICAL: Check for HTTP CONNECT response
                                string responseText = Encoding.UTF8.GetString(responseData, 0, Math.Min(50, responseData.Length));
                                if (responseText.Contains("200 Connection established") || responseText.Contains("HTTP/1.1 200"))
                                {
                                    Log($"✅ {connection.Id}: Server accepted CONNECT, HTTPS tunneling ready");
                                    // Don't forward this to browser - SOCKS5 already handled it
                                    continue;
                                }

                                // Send response to browser
                                await clientStream.WriteAsync(responseData, 0, responseData.Length, ct);
                                await clientStream.FlushAsync();

                                connection.BytesReceived += responseData.Length;
                                Log($"📥 {connection.Id}: Tunnel → Browser: {responseData.Length} bytes");
                            }
                            else
                            {
                                await Task.Delay(10, ct);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Normal cancellation
                    }
                    catch (Exception ex)
                    {
                        Log($"❌ {connection.Id}: Tunnel→Browser error: {ex.Message}");
                    }
                });

                // Wait for either task to complete
                await Task.WhenAny(clientToTunnelTask, tunnelToClientTask);

                cts.Cancel();

                Log($"🔌 {connection.Id}: HTTPS tunneling ended. Stats: ↑{connection.BytesSent} ↓{connection.BytesReceived}");
            }
            catch (Exception ex)
            {
                Log($"❌ {connection.Id}: Forwarding error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if proxy is running
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Get proxy port
        /// </summary>
        public int ProxyPort => _proxyPort;

        /// <summary>
        /// Get active connections count
        /// </summary>
        public int ActiveConnections => _connections.Count;

        private void Log(string message)
        {
            Console.WriteLine($"[SOCKS5] {DateTime.Now:HH:mm:ss} {message}");
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();

            // ✅ Cleanup DNS resources
            _pendingDnsQueries.Clear();
            while (_dnsResponseQueue.TryDequeue(out _)) { }

            // Unsubscribe from events
            if (_tunnelManager != null)
            {
                _tunnelManager.DnsResponseReceived -= OnDnsResponseFromTunnel;
            }
        }

        /// <summary>
        /// SOCKS5 proxy connection info
        /// </summary>
        private class ProxyConnection : IDisposable
        {
            public string Id { get; }
            public TcpClient Client { get; }
            public NetworkStream Stream { get; }
            public string DestinationHost { get; set; }
            public int DestinationPort { get; set; }
            public long BytesSent { get; set; }
            public long BytesReceived { get; set; }

            public ProxyConnection(string id, TcpClient client, TunnelManager tunnelManager)
            {
                Id = id;
                Client = client;
                Stream = client.GetStream();
            }

            public void Dispose()
            {
                Stream?.Close();
                Client?.Close();
            }
        }

        /// <summary>
        /// ✅ DNS query context for tracking
        /// </summary>
        private class DnsQueryContext
        {
            public ushort QueryId { get; set; }
            public IPEndPoint ClientEndpoint { get; set; }
            public byte[] OriginalQuery { get; set; }
            public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// ✅ DNS response data
        /// </summary>
        private class DnsResponse
        {
            public ushort QueryId { get; set; }
            public byte[] Data { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}