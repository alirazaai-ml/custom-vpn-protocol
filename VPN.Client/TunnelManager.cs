using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VPN.Core.Enums;

namespace VPN.Client
{
    /// <summary>
    /// Manages encrypted VPN tunnel with bidirectional data flow
    /// </summary>
    public class TunnelManager : IDisposable
    {
        private readonly ConnectionManager _connectionManager;
        private readonly ClientConfiguration _config;

        private bool _isTunnelActive = false;
        private Thread _tunnelThread;
        private CancellationTokenSource _cancellationTokenSource;

        private readonly ConcurrentQueue<byte[]> _outgoingQueue = new();
        private readonly ConcurrentQueue<byte[]> _incomingQueue = new();

        // ✅ NEW: Response queues per proxy connection
        private readonly ConcurrentDictionary<string, ConcurrentQueue<byte[]>> _responseQueues = new();
        private readonly ConcurrentDictionary<string, TunnelDataContext> _dataContextMap = new();

        // ✅ NEW: DNS tunneling support
        private readonly ConcurrentDictionary<ushort, DateTime> _pendingDnsQueries = new();
        private readonly object _dnsLock = new object();

        // Statistics
        private long _bytesSent = 0;
        private long _bytesReceived = 0;
        private int _packetsSent = 0;
        private int _packetsReceived = 0;
        private int _nextQueryId;

        // Events
        public event EventHandler<TunnelStatus> TunnelStatusChanged;
        public event EventHandler<string> LogMessage;
        public event EventHandler<DnsResponseEventArgs> DnsResponseReceived; // ✅ NEW: DNS response event

        public TunnelManager(ConnectionManager connectionManager, ClientConfiguration config)
        {
            _connectionManager = connectionManager;
            _config = config;
            _cancellationTokenSource = new CancellationTokenSource();

            // Subscribe to connection events
            _connectionManager.ConnectionStatusChanged += OnConnectionStatusChanged;
            _connectionManager.DataReceived += OnDataReceived;
        }

        /// <summary>
        /// Start VPN tunnel
        /// </summary>
        public void StartTunnel()
        {
            if (_isTunnelActive)
            {
                Log("Tunnel is already active");
                return;
            }

            try
            {
                _isTunnelActive = true;
                UpdateTunnelStatus(TunnelStatus.Starting);

                // Clear queues
                while (_outgoingQueue.TryDequeue(out _)) { }
                while (_incomingQueue.TryDequeue(out _)) { }

                // Start tunnel thread
                _tunnelThread = new Thread(TunnelLoop);
                _tunnelThread.IsBackground = true;
                _tunnelThread.Start();

                UpdateTunnelStatus(TunnelStatus.Active);
                Log("VPN tunnel started");
            }
            catch (Exception ex)
            {
                Log($"Failed to start tunnel: {ex.Message}");
                UpdateTunnelStatus(TunnelStatus.Error);
                StopTunnel();
            }
        }

        /// <summary>
        /// Stop VPN tunnel
        /// </summary>
        public void StopTunnel()
        {
            if (!_isTunnelActive) return;

            try
            {
                UpdateTunnelStatus(TunnelStatus.Stopping);
                Log("Stopping VPN tunnel...");

                _isTunnelActive = false;
                _cancellationTokenSource.Cancel();

                _tunnelThread?.Join(2000);

                // Clear queues
                while (_outgoingQueue.TryDequeue(out _)) { }
                while (_incomingQueue.TryDequeue(out _)) { }
                _responseQueues.Clear();
                _dataContextMap.Clear();

                UpdateTunnelStatus(TunnelStatus.Inactive);
                Log("VPN tunnel stopped");
            }
            catch (Exception ex)
            {
                Log($"Error stopping tunnel: {ex.Message}");
            }
        }

        /// <summary>
        /// Main tunnel loop - OPTIMIZED FOR SPEED
        /// </summary>
        private async void TunnelLoop()
        {
            try
            {
                int cleanupCounter = 0;
                
                while (_isTunnelActive)
                {
                    // ✅ FAST: Process both queues concurrently
                    var outgoingTask = ProcessOutgoingQueue();
                    var incomingTask = ProcessIncomingQueue();
                    
                    await Task.WhenAll(outgoingTask, incomingTask);

                    // ✅ Periodic cleanup (every ~200 cycles = ~1 second)
                    if (++cleanupCounter % 200 == 0)
                    {
                        CleanupExpiredDnsQueries();
                        CleanupStaleConnections();
                    }

                    await Task.Delay(5); // Very short delay for high throughput
                }
            }
            catch (Exception ex)
            {
                Log($"Tunnel loop error: {ex.Message}");
                StopTunnel();
            }
        }

        /// <summary>
        /// Process outgoing data queue - FAST
        /// </summary>
        private async Task ProcessOutgoingQueue()
        {
            try
            {
                // ✅ FAST: Process up to 20 packets per cycle
                for (int i = 0; i < 20 && _outgoingQueue.TryDequeue(out byte[] data); i++)
                {
                    if (data != null && data.Length > 0)
                    {
                        await _connectionManager.SendDataAsync(data);

                        // Update statistics
                        _bytesSent += data.Length;
                        _packetsSent++;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing outgoing queue: {ex.Message}");
            }
        }

        /// <summary>
        /// Process incoming data queue - Route responses to connections
        /// </summary>
        private async Task ProcessIncomingQueue()
        {
            try
            {
                // Process up to 20 packets per cycle for faster throughput
                for (int i = 0; i < 20 && _incomingQueue.TryDequeue(out byte[] data); i++)
                {
                    if (data != null && data.Length > 0)
                    {
                        // ✅ Check if this is a DNS response
                        if (IsDnsResponse(data))
                        {
                            ProcessDnsResponse(data);
                            continue;
                        }

                        // ✅ Process regular data routing
                        ProcessRegularDataResponse(data);

                        // Update statistics
                        _bytesReceived += data.Length;
                        _packetsReceived++;
                    }
                }
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log($"Error processing incoming queue: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NEW: Check if incoming data is a DNS response
        /// </summary>
        private bool IsDnsResponse(byte[] data)
        {
            try
            {
                // Check for DNS response pattern: first 2 bytes are query ID, followed by DNS response
                return data.Length >= 4 && 
                       (data[2] & 0x80) == 0x80; // DNS response flag set
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ✅ NEW: Process DNS response from server
        /// </summary>
        private void ProcessDnsResponse(byte[] data)
        {
            try
            {
                if (data.Length < 4) // Minimum: 2 bytes query ID + 2 bytes DNS header
                {
                    Log("❌ Invalid DNS response: too short");
                    return;
                }

                // Extract query ID from first 2 bytes
                ushort queryId = BitConverter.ToUInt16(data, 0);
                
                // Extract DNS response data (skip 2-byte query ID)
                byte[] dnsResponse = new byte[data.Length - 2];
                Array.Copy(data, 2, dnsResponse, 0, dnsResponse.Length);

                Log($"📥 DNS response received for query {queryId} ({dnsResponse.Length} bytes)");

                // Check if we're expecting this response
                lock (_dnsLock)
                {
                    if (_pendingDnsQueries.ContainsKey(queryId))
                    {
                        _pendingDnsQueries.TryRemove(queryId, out _);
                        
                        // Trigger DNS response event
                        DnsResponseReceived?.Invoke(this, new DnsResponseEventArgs(queryId, dnsResponse));

                        Log($"✅ DNS response {queryId} forwarded to LocalProxy");
                    }
                    else
                    {
                        Log($"⚠️ Unexpected DNS response for query {queryId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error processing DNS response: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NEW: Process regular (non-DNS) data response
        /// </summary>
        private void ProcessRegularDataResponse(byte[] data)
        {
            try
            {
                // Route responses to response queues
                if (data.Length >= 4)
                {
                    // Extract hash from first 4 bytes
                    int hash = BitConverter.ToInt32(data, 0);
                    
                    // Remove hash from data
                    byte[] actualData = new byte[data.Length - 4];
                    Buffer.BlockCopy(data, 4, actualData, 0, actualData.Length);
                    
                    // Find matching connection by hash
                    string matchedConnectionId = null;
                    foreach (var kvp in _dataContextMap)
                    {
                        if (kvp.Key.GetHashCode() == hash)
                        {
                            matchedConnectionId = kvp.Key;
                            break;
                        }
                    }
                    
                    if (matchedConnectionId != null)
                    {
                        // Route to specific connection
                        QueueResponseForConnection(matchedConnectionId, actualData);
                        Log($"📥 Routed {actualData.Length} bytes to connection {matchedConnectionId}");
                    }
                    else
                    {
                        // No match found - broadcast to all active connections
                        if (_responseQueues.Count > 0)
                        {
                            var firstConnection = _responseQueues.First();
                            firstConnection.Value.Enqueue(actualData);
                            Log($"📥 Broadcast {actualData.Length} bytes to {firstConnection.Key}");
                        }
                    }
                }
                else
                {
                    // Data too small for hash - broadcast
                    if (_responseQueues.Count > 0)
                    {
                        var firstConnection = _responseQueues.First();
                        firstConnection.Value.Enqueue(data);
                        Log($"📥 Received {data.Length} bytes (no hash, broadcast)");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Error processing regular data response: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NEW: Clean up expired DNS queries
        /// </summary>
        private void CleanupExpiredDnsQueries()
        {
            try
            {
                var cutoff = DateTime.Now.AddMinutes(-2);
                var expired = new List<ushort>();

                lock (_dnsLock)
                {
                    foreach (var kvp in _pendingDnsQueries)
                    {
                        if (kvp.Value < cutoff)
                        {
                            expired.Add(kvp.Key);
                        }
                    }

                    foreach (var queryId in expired)
                    {
                        _pendingDnsQueries.TryRemove(queryId, out _);
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
        /// Send data through tunnel
        /// </summary>
        public void SendData(byte[] data)
        {
            if (!_isTunnelActive)
            {
                Log("Tunnel not active, cannot send data");
                return;
            }

            // Queue data for sending
            _outgoingQueue.Enqueue(data);
        }

        /// <summary>
        /// ✅ NEW: Send data with connection context
        /// </summary>
        public void SendDataWithContext(string connectionId, byte[] data)
        {
            if (!_isTunnelActive)
            {
                Log("Tunnel not active, cannot send data");
                return;
            }

            try
            {
                // Store context for response routing
                _dataContextMap.TryAdd(connectionId, new TunnelDataContext 
                { 
                    ConnectionId = connectionId, 
                    Timestamp = DateTime.Now 
                });

                // ✅ Prepend connection ID to data (simple protocol)
                // Format: [4 bytes: connection ID hash][data]
                byte[] contextData = PrependConnectionId(connectionId, data);
                
                _outgoingQueue.Enqueue(contextData);
                
                Log($"📤 Queued {data.Length} bytes for connection {connectionId}");
            }
            catch (Exception ex)
            {
                Log($"Error sending data with context: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NEW: Receive data for specific connection
        /// </summary>
        public async Task<byte[]> ReceiveDataForConnection(string connectionId, int timeoutMs = 100)
        {
            if (!_isTunnelActive)
                return null;

            try
            {
                // Get or create queue for this connection
                if (!_responseQueues.TryGetValue(connectionId, out var queue))
                {
                    queue = new ConcurrentQueue<byte[]>();
                    _responseQueues[connectionId] = queue;
                }

                // ✅ FAST: Try immediate dequeue (no waiting)
                if (queue.TryDequeue(out var data))
                {
                    return data;
                }

                // Only wait if timeout > 0
                if (timeoutMs > 0)
                {
                    await Task.Delay(Math.Min(timeoutMs, 10)); // Max 10ms wait
                    
                    // Try one more time
                    if (queue.TryDequeue(out data))
                    {
                        return data;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error receiving data for connection: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// ✅ NEW: Queue response for specific connection
        /// </summary>
        private void QueueResponseForConnection(string connectionId, byte[] data)
        {
            if (!_responseQueues.TryGetValue(connectionId, out var queue))
            {
                queue = new ConcurrentQueue<byte[]>();
                _responseQueues[connectionId] = queue;
            }

            queue.Enqueue(data);
        }

        /// <summary>
        /// ✅ NEW: Prepend connection ID to data
        /// </summary>
        private byte[] PrependConnectionId(string connectionId, byte[] data)
        {
            // Simple protocol: [4 bytes: hash of connection ID][original data]
            int hash = connectionId.GetHashCode();
            byte[] result = new byte[4 + data.Length];
            
            BitConverter.GetBytes(hash).CopyTo(result, 0);
            data.CopyTo(result, 4);
            
            return result;
        }

        /// <summary>
        /// ✅ NEW: Extract connection ID from data
        /// </summary>
        private string ExtractConnectionId(byte[] data)
        {
            if (data == null || data.Length < 4)
                return null;

            try
            {
                // Extract hash from first 4 bytes
                int hash = BitConverter.ToInt32(data, 0);
                
                // Find matching connection ID
                foreach (var context in _dataContextMap)
                {
                    if (context.Key.GetHashCode() == hash)
                    {
                        return context.Key;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// ✅ NEW: Clean up old connection contexts
        /// </summary>
        public void CleanupStaleConnections()
        {
            var cutoff = DateTime.Now.AddMinutes(-5);
            
            foreach (var kvp in _dataContextMap.ToArray())
            {
                if (kvp.Value.Timestamp < cutoff)
                {
                    _dataContextMap.TryRemove(kvp.Key, out _);
                    Log($"🧹 Removed stale connection context: {kvp.Key}");
                }
            }
        }

        /// <summary>
        /// Send DNS query through tunnel
        /// </summary>
        public void SendDnsQuery(byte[] dnsQuery)
        {
            if (!_isTunnelActive)
            {
                Log("Tunnel not active, cannot send DNS query");
                return;
            }

            try
            {
                Log($"🔍 Sending DNS query through tunnel ({dnsQuery.Length} bytes)");
                
                // Use UDP-like framing: [2 bytes: query ID][DNS data]
                ushort queryId = (ushort)(Interlocked.Increment(ref _nextQueryId) & 0xFFFF);
                byte[] framedQuery = new byte[2 + dnsQuery.Length];
                
                BitConverter.GetBytes(queryId).CopyTo(framedQuery, 0);
                dnsQuery.CopyTo(framedQuery, 2);

                // Mark this query as pending
                lock (_dnsLock)
                {
                    _pendingDnsQueries[queryId] = DateTime.Now;
                }
                
                _outgoingQueue.Enqueue(framedQuery);
                
                _bytesSent += framedQuery.Length;
                _packetsSent++;
            }
            catch (Exception ex)
            {
                Log($"Error sending DNS query: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NEW: Send DNS query through tunnel asynchronously with tracking
        /// </summary>
        public async Task SendDnsQueryAsync(byte[] dnsQuery, ushort queryId)
        {
            if (!_isTunnelActive)
            {
                Log("Tunnel not active, cannot send DNS query");
                return;
            }

            try
            {
                Log($"🔍 Sending DNS query {queryId} through tunnel ({dnsQuery.Length} bytes)");
                
                // Create framed query with custom query ID
                byte[] framedQuery = new byte[2 + dnsQuery.Length];
                BitConverter.GetBytes(queryId).CopyTo(framedQuery, 0);
                dnsQuery.CopyTo(framedQuery, 2);

                // Mark this query as pending
                lock (_dnsLock)
                {
                    _pendingDnsQueries[queryId] = DateTime.Now;
                }
                
                _outgoingQueue.Enqueue(framedQuery);
                
                _bytesSent += framedQuery.Length;
                _packetsSent++;

                Log($"✅ DNS query {queryId} queued for transmission");
            }
            catch (Exception ex)
            {
                Log($"❌ Error sending DNS query {queryId}: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ NEW: Send HTTP CONNECT request through tunnel for HTTPS
        /// </summary>
        public void SendHttpConnectRequest(string connectionId, string host, int port)
        {
            if (!_isTunnelActive)
            {
                Log("Tunnel not active, cannot send CONNECT request");
                return;
            }

            try
            {
                // Create HTTP CONNECT request
                string connectRequest = $"CONNECT {host}:{port} HTTP/1.1\r\n" +
                                       $"Host: {host}:{port}\r\n" +
                                       $"Proxy-Connection: keep-alive\r\n" +
                                       $"\r\n";

                byte[] data = Encoding.UTF8.GetBytes(connectRequest);

                // Store context
                _dataContextMap.TryAdd(connectionId, new TunnelDataContext
                {
                    ConnectionId = connectionId,
                    Timestamp = DateTime.Now,
                    IsHttps = true,
                    RemoteHost = host,
                    RemotePort = port
                });

                // Send through tunnel
                byte[] contextData = PrependConnectionId(connectionId, data);
                _outgoingQueue.Enqueue(contextData);

                Log($"📤 Sent HTTPS CONNECT for {host}:{port} via {connectionId}");
            }
            catch (Exception ex)
            {
                Log($"❌ Error sending CONNECT request: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle connection status changes
        /// </summary>
        private void OnConnectionStatusChanged(object sender, ConnectionStatus status)
        {
            switch (status)
            {
                case ConnectionStatus.Connected:
                    if (!_isTunnelActive && _config.AutoConnect)
                    {
                        StartTunnel();
                    }
                    break;

                case ConnectionStatus.Disconnected:
                case ConnectionStatus.Error:
                    StopTunnel();
                    break;
            }
        }

        /// <summary>
        /// Handle data received from connection
        /// </summary>
        private void OnDataReceived(object sender, byte[] data)
        {
            if (_isTunnelActive && data != null && data.Length > 0)
            {
                _incomingQueue.Enqueue(data);
            }
        }

        /// <summary>
        /// Update tunnel status
        /// </summary>
        private void UpdateTunnelStatus(TunnelStatus status)
        {
            TunnelStatusChanged?.Invoke(this, status);
        }

        /// <summary>
        /// Log message
        /// </summary>
        private void Log(string message)
        {
            Console.WriteLine($"[Tunnel] {DateTime.Now:HH:mm:ss} {message}");
            LogMessage?.Invoke(this, message);
        }

        public (long bytesSent, long bytesReceived, int packetsSent, int packetsReceived) GetStatistics()
        {
            return (_bytesSent, _bytesReceived, _packetsSent, _packetsReceived);
        }

        public void ResetStatistics()
        {
            _bytesSent = 0;
            _bytesReceived = 0;
            _packetsSent = 0;
            _packetsReceived = 0;
        }

        public bool IsTunnelActive => _isTunnelActive;

        public TunnelStatus Status => _isTunnelActive ? TunnelStatus.Active : TunnelStatus.Inactive;

        public void Dispose()
        {
            StopTunnel();
            _cancellationTokenSource?.Dispose();
            
            // ✅ Cleanup DNS resources
            lock (_dnsLock)
            {
                _pendingDnsQueries.Clear();
            }
        }

        /// <summary>
        /// Data context for connection tracking
        /// </summary>
        private class TunnelDataContext
        {
            public string ConnectionId { get; set; }
            public DateTime Timestamp { get; set; }
            public bool IsHttps { get; set; }
            public string RemoteHost { get; set; }
            public int RemotePort { get; set; }
        }
    }

    /// <summary>
    /// Tunnel status enumeration
    /// </summary>
    public enum TunnelStatus
    {
        Inactive,
        Starting,
        Active,
        Stopping,
        Error
    }

    /// <summary>
    /// DNS response event arguments
    /// </summary>
    public class DnsResponseEventArgs : EventArgs
    {
        public ushort QueryId { get; }
        public byte[] ResponseData { get; }

        public DnsResponseEventArgs(ushort queryId, byte[] responseData)
        {
            QueryId = queryId;
            ResponseData = responseData;
        }
    }
}