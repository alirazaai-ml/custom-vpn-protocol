using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace VPN.Server
{
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
                Console.WriteLine("Server is already running.");
                return;
            }

            if (!_config.Validate())
            {
                Console.WriteLine("Configuration validation failed. Cannot start server.");
                return;
            }

            try
            {
                // Initialize
                _startTime = DateTime.Now;
                _isRunning = true;

                // Create TCP listener
                IPAddress bindAddress = IPAddress.Parse(_config.BindAddress);
                _tcpListener = new TcpListener(bindAddress, _config.Port);

                // Start listener
                _tcpListener.Start();
                Console.WriteLine($"VPN Server started on {bindAddress}:{_config.Port}");

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

                Console.WriteLine("VPN Server is ready and listening for connections...");
                DisplayServerInfo();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start server: {ex.Message}");
                Stop();
            }
        }

        /// <summary>
        /// Stop the VPN server
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            Console.WriteLine("Stopping VPN Server...");
            _isRunning = false;
            _cancellationTokenSource.Cancel();

            // Stop listening
            try
            {
                _tcpListener?.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping listener: {ex.Message}");
            }

            // Stop all client handlers
            foreach (var handler in _clientHandlers.ToArray())
            {
                handler.Stop();
            }
            _clientHandlers.Clear();

            // Stop packet forwarder
            _packetForwarder.Stop();

            // Wait for threads to finish
            _listenerThread?.Join(1000);
            _cleanupThread?.Join(1000);

            Console.WriteLine("VPN Server stopped.");
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

                        // Check if we can accept more clients
                        if (_clientHandlers.Count >= _config.MaxClients)
                        {
                            Console.WriteLine($"Max clients ({_config.MaxClients}) reached. Rejecting connection.");
                            tcpClient.Close();
                            continue;
                        }

                        // Create and start client handler
                        var handler = new ClientHandler(tcpClient, _sessionManager, _packetForwarder, _config);
                        _clientHandlers.Add(handler);
                        handler.Start();

                        _totalConnections++;
                        Console.WriteLine($"New client connected. Total connections: {_totalConnections}");
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
                Console.WriteLine($"Listener error: {ex.Message}");
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
                        Console.WriteLine($"Cleaned up {expiredSessions.Count} expired sessions");
                    }

                    // Remove disconnected client handlers
                    var disconnectedHandlers = _clientHandlers.FindAll(h => !h.IsRunning);
                    foreach (var handler in disconnectedHandlers)
                    {
                        _clientHandlers.Remove(handler);
                        handler.Dispose();
                    }

                    if (disconnectedHandlers.Count > 0)
                    {
                        Console.WriteLine($"Removed {disconnectedHandlers.Count} disconnected client handlers");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Cleanup error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Get server statistics
        /// </summary>
        public void DisplayServerInfo()
        {
            var stats = _sessionManager.GetSessionStatistics();
            var forwardStats = _packetForwarder.GetStatistics();
            var uptime = DateTime.Now - _startTime;

            Console.WriteLine("\n=== VPN Server Information ===");
            Console.WriteLine($"Uptime: {uptime:hh\\:mm\\:ss}");
            Console.WriteLine($"Total Connections: {_totalConnections}");
            Console.WriteLine($"Active Sessions: {stats.active}/{stats.total}");
            Console.WriteLine($"Connected Clients: {_clientHandlers.Count}");
            Console.WriteLine($"Total Bytes Forwarded: {forwardStats.totalBytes:N0}");
            Console.WriteLine($"Total Packets Forwarded: {forwardStats.totalPackets:N0}");
            Console.WriteLine("==============================\n");
        }

        /// <summary>
        /// Get list of connected clients
        /// </summary>
        public List<ClientHandler> GetConnectedClients()
        {
            return new List<ClientHandler>(_clientHandlers);
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

        public void Dispose()
        {
            Stop();
            _packetForwarder?.Dispose();
            _cancellationTokenSource?.Dispose();
            _tcpListener = null;
        }
    }
}