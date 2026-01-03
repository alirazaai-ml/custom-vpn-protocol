using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace VPN.Server
{
    // Event delegate for client events
    public delegate void ClientEventHandler(object sender, ClientEventArgs e);

    // Event delegate for log messages
    public delegate void LogMessageEventHandler(object sender, LogMessageEventArgs e);

    // Event delegate for statistics
    public delegate void StatisticsUpdatedEventHandler(object sender, StatisticsEventArgs e);

    // Event delegate for user approval requests
    public delegate void UserApprovalRequestEventHandler(object sender, UserApprovalRequestEventArgs e);

    // Event args classes
    public class ClientEventArgs : EventArgs
    {
        public string ClientId { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Action { get; set; } = "connected"; // "connected" or "disconnected"
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class LogMessageEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public string Level { get; set; } = "INFO"; // "INFO", "WARN", "ERROR"
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class StatisticsEventArgs : EventArgs
    {
        public int ConnectedClients { get; set; }
        public long TotalBytesForwarded { get; set; }
        public int TotalPacketsForwarded { get; set; }
        public TimeSpan Uptime { get; set; }
        public int ActiveSessions { get; set; }
        public int TotalSessions { get; set; }
    }

    // NEW: User approval request event args
    public class UserApprovalRequestEventArgs : EventArgs
    {
        public string Username { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public TaskCompletionSource<bool> ApprovalResult { get; set; } = new TaskCompletionSource<bool>();
    }

    /// <summary>
    /// Main VPN server controller
    /// </summary>
    public class VpnServer : IDisposable
    {
        private readonly ServerConfiguration _config;
        private readonly SessionManager _sessionManager;
        private readonly PacketForwarder _packetForwarder;
        private readonly List<ClientHandler> _clientHandlers = new();

        private TcpListener _tcpListener;
        private bool _isRunning = false;
        private Thread _listenerThread;
        private Thread _cleanupThread;
        private CancellationTokenSource _cancellationTokenSource;

        // Server statistics
        private int _totalConnections = 0;
        private DateTime _startTime;

        // Connected clients tracking
        private Dictionary<string, ClientInfo> _connectedClients = new Dictionary<string, ClientInfo>();

        // Client info class
        public class ClientInfo
        {
            public string ClientId { get; set; } = string.Empty;
            public string IpAddress { get; set; } = string.Empty;
            public DateTime ConnectedAt { get; set; } = DateTime.Now;
            public long BytesSent { get; set; }
            public long BytesReceived { get; set; }
            public string Status { get; set; } = "Connected";
            public ClientHandler? Handler { get; set; }
        }

        // Events
        public event ClientEventHandler? ClientConnected;
        public event ClientEventHandler? ClientDisconnected;
        public event LogMessageEventHandler? LogMessage;
        public event StatisticsUpdatedEventHandler? StatisticsUpdated;
        public event UserApprovalRequestEventHandler? UserApprovalRequested; // NEW: User approval event

        public VpnServer(ServerConfiguration config = null)
        {
            _config = config ?? new ServerConfiguration();
            _sessionManager = new SessionManager();
            _packetForwarder = new PacketForwarder(_sessionManager);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Start the VPN server
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                OnLogMessage("Server is already running.", "WARN");
                return;
            }

            if (!_config.Validate())
            {
                OnLogMessage("Configuration validation failed. Cannot start server.", "ERROR");
                return;
            }

            try
            {
                // Initialize
                _startTime = DateTime.Now;
                _isRunning = true;
                _connectedClients.Clear();

                // Create TCP listener
                IPAddress bindAddress = IPAddress.Parse(_config.BindAddress);
                _tcpListener = new TcpListener(bindAddress, _config.Port);

                // Start listener
                _tcpListener.Start();
                OnLogMessage($"VPN Server started on {bindAddress}:{_config.Port}", "INFO");

                // Start packet forwarder
                _packetForwarder.Start();

                // Start listener thread
                _listenerThread = new Thread(ListenForClients);
                _listenerThread.IsBackground = true;
                _listenerThread.Start();

                // Start cleanup thread
                _cleanupThread = new Thread(CleanupLoop);
                _cleanupThread.IsBackground = true;
                _cleanupThread.Start();

                OnLogMessage("VPN Server is ready and listening for connections...", "INFO");

                // Update statistics
                UpdateStatistics();

                // Display initial info
                DisplayServerInfo();
            }
            catch (Exception ex)
            {
                OnLogMessage($"Failed to start server: {ex.Message}", "ERROR");
                Stop();
            }
        }

        /// <summary>
        /// Start the VPN server asynchronously
        /// </summary>
        public async Task<bool> StartAsync()
        {
            try
            {
                await Task.Run(() => Start());
                return _isRunning;
            }
            catch (Exception ex)
            {
                OnLogMessage($"Failed to start server asynchronously: {ex.Message}", "ERROR");
                return false;
            }
        }

        /// <summary>
        /// Stop the VPN server
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            OnLogMessage("Stopping VPN Server...", "INFO");
            _isRunning = false;
            _cancellationTokenSource.Cancel();

            // Stop listening
            try
            {
                _tcpListener?.Stop();
            }
            catch (Exception ex)
            {
                OnLogMessage($"Error stopping listener: {ex.Message}", "ERROR");
            }

            // Stop all client handlers and trigger disconnect events
            var clientArray = _clientHandlers.ToArray();
            foreach (var handler in clientArray)
            {
                try
                {
                    var clientInfo = GetClientInfoByHandler(handler);
                    if (clientInfo != null)
                    {
                        OnClientDisconnected(clientInfo.ClientId, clientInfo.IpAddress);
                    }
                    handler.Stop();
                }
                catch (Exception ex)
                {
                    OnLogMessage($"Error stopping client handler: {ex.Message}", "ERROR");
                }
            }
            _clientHandlers.Clear();
            _connectedClients.Clear();

            // Stop packet forwarder
            _packetForwarder.Stop();

            // Wait for threads to finish
            _listenerThread?.Join(1000);
            _cleanupThread?.Join(1000);

            OnLogMessage("VPN Server stopped.", "INFO");
            UpdateStatistics();
        }

        /// <summary>
        /// Stop the VPN server asynchronously
        /// </summary>
        public async Task StopAsync()
        {
            await Task.Run(() => Stop());
        }

        /// <summary>
        /// Listen for incoming client connections
        /// </summary>
        private void ListenForClients()
        {
            try
            {
                while (_isRunning)
                {
                    // Check for pending connections
                    if (_tcpListener.Pending())
                    {
                        // Accept client
                        TcpClient tcpClient = _tcpListener.AcceptTcpClient();
                        string clientIp = ((IPEndPoint)tcpClient.Client.RemoteEndPoint!).Address.ToString();

                        // Check if we can accept more clients
                        if (_clientHandlers.Count >= _config.MaxClients)
                        {
                            OnLogMessage($"Max clients ({_config.MaxClients}) reached. Rejecting connection from {clientIp}.", "WARN");
                            tcpClient.Close();
                            continue;
                        }

                        // Create and start client handler
                        var handler = new ClientHandler(tcpClient, _sessionManager, _packetForwarder, _config, this);

                        // Set up handler events
                        handler.ClientIdentified += OnClientIdentifiedHandler;
                        handler.ClientDisconnected += OnClientDisconnectedHandler;
                        handler.LogMessage += OnHandlerLogMessage;
                        handler.DataTransferred += OnHandlerDataTransferred;

                        _clientHandlers.Add(handler);
                        handler.Start();

                        _totalConnections++;
                        OnLogMessage($"New client connected from {clientIp}. Total connections: {_totalConnections}", "INFO");
                    }
                    else
                    {
                        Thread.Sleep(100); // Small delay to prevent CPU spinning
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                OnLogMessage($"Listener error: {ex.Message}", "ERROR");
            }
        }

        /// <summary>
        /// Cleanup loop for expired sessions and disconnected clients
        /// </summary>
        private void CleanupLoop()
        {
            while (_isRunning)
            {
                try
                {
                    Thread.Sleep(30000); // Run every 30 seconds

                    if (!_isRunning) break;

                    // Remove expired sessions
                    var expiredSessions = _sessionManager.RemoveExpiredSessions(_config.SessionTimeout / 1000);
                    if (expiredSessions.Count > 0)
                    {
                        OnLogMessage($"Cleaned up {expiredSessions.Count} expired sessions", "INFO");
                    }

                    // Remove disconnected client handlers
                    var disconnectedHandlers = _clientHandlers.FindAll(h => !h.IsRunning);
                    foreach (var handler in disconnectedHandlers)
                    {
                        _clientHandlers.Remove(handler);
                        var clientInfo = GetClientInfoByHandler(handler);
                        if (clientInfo != null)
                        {
                            OnClientDisconnected(clientInfo.ClientId, clientInfo.IpAddress);
                        }
                        handler.Dispose();
                    }

                    if (disconnectedHandlers.Count > 0)
                    {
                        OnLogMessage($"Removed {disconnectedHandlers.Count} disconnected client handlers", "INFO");
                    }

                    // Update statistics
                    UpdateStatistics();
                }
                catch (Exception ex)
                {
                    OnLogMessage($"Cleanup error: {ex.Message}", "ERROR");
                }
            }
        }

        /// <summary>
        /// Handle client identification from handler
        /// </summary>
        private void OnClientIdentifiedHandler(object? sender, ClientIdentifiedEventArgs e)
        {
            var clientInfo = new ClientInfo
            {
                ClientId = e.ClientId,
                IpAddress = e.IpAddress,
                ConnectedAt = e.Timestamp,
                Status = "Connected",
                Handler = sender as ClientHandler
            };

            lock (_connectedClients)
            {
                _connectedClients[e.ClientId] = clientInfo;
            }

            OnClientConnected(e.ClientId, e.IpAddress);
        }

        /// <summary>
        /// Handle client disconnect from handler
        /// </summary>
        private void OnClientDisconnectedHandler(object? sender, ClientDisconnectedEventArgs e)
        {
            lock (_connectedClients)
            {
                _connectedClients.Remove(e.ClientId);
            }

            OnClientDisconnected(e.ClientId, e.IpAddress);
        }

        /// <summary>
        /// Handle log messages from handlers
        /// </summary>
        private void OnHandlerLogMessage(object? sender, ClientLogMessageEventArgs e)
        {
            OnLogMessage($"[{e.ClientId}] {e.Message}", e.Level);
        }

        /// <summary>
        /// Handle data transfer events from handlers
        /// </summary>
        private void OnHandlerDataTransferred(object? sender, ClientDataTransferEventArgs e)
        {
            // Update client statistics
            lock (_connectedClients)
            {
                if (_connectedClients.TryGetValue(e.ClientId, out var clientInfo))
                {
                    clientInfo.BytesSent += e.BytesSent;
                    clientInfo.BytesReceived += e.BytesReceived;
                }
            }

            UpdateStatistics();
        }

        /// <summary>
        /// Get client info by handler
        /// </summary>
        private ClientInfo? GetClientInfoByHandler(ClientHandler handler)
        {
            lock (_connectedClients)
            {
                foreach (var client in _connectedClients.Values)
                {
                    if (client.Handler == handler)
                    {
                        return client;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Display server information
        /// </summary>
        public void DisplayServerInfo()
        {
            var stats = _sessionManager.GetSessionStatistics();
            var forwardStats = _packetForwarder.GetStatistics();
            var uptime = DateTime.Now - _startTime;

            string info = $"\n=== VPN Server Information ===\n" +
                         $"Uptime: {uptime:hh\\:mm\\:ss}\n" +
                         $"Total Connections: {_totalConnections}\n" +
                         $"Active Sessions: {stats.active}/{stats.total}\n" +
                         $"Connected Clients: {_clientHandlers.Count}\n" +
                         $"Total Bytes Forwarded: {forwardStats.totalBytes:N0}\n" +
                         $"Total Packets Forwarded: {forwardStats.totalPackets:N0}\n" +
                         "==============================\n";

            Console.WriteLine(info);
            OnLogMessage(info.Trim(), "INFO");
        }

        /// <summary>
        /// Get list of connected clients
        /// </summary>
        public List<ClientHandler> GetConnectedClients()
        {
            return new List<ClientHandler>(_clientHandlers);
        }

        /// <summary>
        /// Get connected clients information
        /// </summary>
        public List<ClientInfo> GetConnectedClientsInfo()
        {
            lock (_connectedClients)
            {
                return new List<ClientInfo>(_connectedClients.Values);
            }
        }

        /// <summary>
        /// Get session manager
        /// </summary>
        public SessionManager GetSessionManager() => _sessionManager;

        /// <summary>
        /// Get packet forwarder
        /// </summary>
        public PacketForwarder GetPacketForwarder() => _packetForwarder;

        /// <summary>
        /// Check if server is running
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Get server uptime
        /// </summary>
        public TimeSpan GetUptime() => DateTime.Now - _startTime;

        /// <summary>
        /// Get total connections count
        /// </summary>
        public int TotalConnections => _totalConnections;

        /// <summary>
        /// Get current connected clients count
        /// </summary>
        public int ConnectedClientsCount => _clientHandlers.Count;

        // ====================== EVENT TRIGGERS ======================

        /// <summary>
        /// Trigger client connected event
        /// </summary>
        protected virtual void OnClientConnected(string clientId, string ipAddress)
        {
            ClientConnected?.Invoke(this, new ClientEventArgs
            {
                ClientId = clientId,
                IpAddress = ipAddress,
                Action = "connected",
                Timestamp = DateTime.Now
            });

            UpdateStatistics();
        }

        /// <summary>
        /// Trigger client disconnected event
        /// </summary>
        protected virtual void OnClientDisconnected(string clientId, string ipAddress)
        {
            lock (_connectedClients)
            {
                _connectedClients.Remove(clientId);
            }

            ClientDisconnected?.Invoke(this, new ClientEventArgs
            {
                ClientId = clientId,
                IpAddress = ipAddress,
                Action = "disconnected",
                Timestamp = DateTime.Now
            });

            UpdateStatistics();
        }

        /// <summary>
        /// Trigger log message event
        /// </summary>
        protected virtual void OnLogMessage(string message, string level = "INFO")
        {
            LogMessage?.Invoke(this, new LogMessageEventArgs
            {
                Message = message,
                Level = level,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Update and trigger statistics event
        /// </summary>
        private void UpdateStatistics()
        {
            try
            {
                var stats = _sessionManager.GetSessionStatistics();
                var forwardStats = _packetForwarder.GetStatistics();

                StatisticsUpdated?.Invoke(this, new StatisticsEventArgs
                {
                    ConnectedClients = _clientHandlers.Count,
                    TotalBytesForwarded = forwardStats.totalBytes,
                    TotalPacketsForwarded = forwardStats.totalPackets,
                    Uptime = DateTime.Now - _startTime,
                    ActiveSessions = stats.active,
                    TotalSessions = stats.total
                });
            }
            catch (Exception ex)
            {
                OnLogMessage($"Error updating statistics: {ex.Message}", "ERROR");
            }
        }

        /// <summary>
        /// Get server statistics for dashboard
        /// </summary>
        public StatisticsEventArgs GetServerStatistics()
        {
            var stats = _sessionManager.GetSessionStatistics();
            var forwardStats = _packetForwarder.GetStatistics();

            return new StatisticsEventArgs
            {
                ConnectedClients = _clientHandlers.Count,
                TotalBytesForwarded = forwardStats.totalBytes,
                TotalPacketsForwarded = forwardStats.totalPackets,
                Uptime = DateTime.Now - _startTime,
                ActiveSessions = stats.active,
                TotalSessions = stats.total
            };
        }

        /// <summary>
        /// Trigger user approval request event
        /// </summary>
        public async Task<bool> RequestUserApproval(string username, string clientId, string ipAddress)
        {
            try
            {
                OnLogMessage($"🔍 RequestUserApproval called for user: {username}", "DEBUG");
                OnLogMessage($"🔍 ClientId: {clientId}, IP: {ipAddress}", "DEBUG");
                
                // ✅ Check if anyone is listening to the event
                if (UserApprovalRequested == null)
                {
                    OnLogMessage($"❌ CRITICAL: No listeners for UserApprovalRequested event!", "ERROR");
                    OnLogMessage($"❌ This means the dashboard is not properly subscribed", "ERROR");
                    return false; // Deny by default if no one is listening
                }

                var approvalArgs = new UserApprovalRequestEventArgs
                {
                    Username = username,
                    ClientId = clientId,
                    IpAddress = ipAddress,
                    ApprovalResult = new TaskCompletionSource<bool>()
                };

                OnLogMessage($"📡 Triggering UserApprovalRequested event...", "DEBUG");
                
                // Trigger event
                UserApprovalRequested?.Invoke(this, approvalArgs);
                
                OnLogMessage($"⏳ Waiting for approval decision...", "DEBUG");

                // Wait for approval decision with timeout
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5)); // 5 minute timeout
                var completedTask = await Task.WhenAny(approvalArgs.ApprovalResult.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    OnLogMessage($"⏰ Approval timeout for user '{username}' - auto-denying", "WARN");
                    return false;
                }

                bool approved = await approvalArgs.ApprovalResult.Task;
                
                OnLogMessage($"✅ Approval decision received for '{username}': {approved}", "INFO");
                return approved;
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Error in RequestUserApproval: {ex.Message}", "ERROR");
                OnLogMessage($"📍 Exception type: {ex.GetType().Name}", "ERROR");
                return false; // Deny by default on error
            }
        }

        public void Dispose()
        {
            Stop();
            _packetForwarder?.Dispose();
            _cancellationTokenSource?.Dispose();
            _tcpListener = null;
        }
    }
}