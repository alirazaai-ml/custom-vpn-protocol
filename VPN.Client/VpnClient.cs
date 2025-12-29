using System;
using System.Threading.Tasks;
using VPN.Core.Enums;

namespace VPN.Client
{
    /// <summary>
    /// Main VPN client controller
    /// </summary>
    public class VpnClient : IDisposable
    {
        private readonly ClientConfiguration _config;
        private readonly ConnectionManager _connectionManager;
        private readonly TunnelManager _tunnelManager;
        private readonly LocalProxy _localProxy;

        private bool _isRunning = false;

        // Events
        public event EventHandler<ConnectionStatus> ConnectionStatusChanged;
        public event EventHandler<string> LogMessage;
        public event EventHandler<TunnelStatus> TunnelStatusChanged;

        public VpnClient(ClientConfiguration config = null)
        {
            _config = config ?? ClientConfiguration.LoadFromFile();
            _connectionManager = new ConnectionManager(_config);
            _tunnelManager = new TunnelManager(_connectionManager, _config);
            _localProxy = new LocalProxy(_tunnelManager, _config);

            // Subscribe to events
            _connectionManager.ConnectionStatusChanged += OnConnectionStatusChanged;
            _connectionManager.LogMessage += OnLogMessage;
            _tunnelManager.TunnelStatusChanged += OnTunnelStatusChanged;
            _tunnelManager.LogMessage += OnLogMessage;
        }

        /// <summary>
        /// Connect to VPN server
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            if (_isRunning)
            {
                Log("VPN client is already running");
                return true;
            }

            try
            {
                Log("Starting VPN client...");
                _isRunning = true;

                // Validate configuration
                if (!_config.Validate())
                {
                    Log("Configuration validation failed");
                    return false;
                }

                _config.DisplaySummary();

                // Connect to server
                bool connected = await _connectionManager.ConnectAsync();
                if (!connected)
                {
                    Log("Failed to connect to server");
                    return false;
                }

                // Start local proxy
                if (_config.EnableLocalProxy)
                {
                    _localProxy.Start();
                }

                Log("VPN client started successfully");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Failed to start VPN client: {ex.Message}");
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// Disconnect from VPN server
        /// </summary>
        public void Disconnect(string reason = "Client requested disconnect")
        {
            if (!_isRunning) return;

            try
            {
                Log($"Disconnecting VPN client: {reason}");

                // Stop tunnel first
                _tunnelManager.StopTunnel();

                // Stop local proxy
                _localProxy.Stop();

                // Disconnect from server
                _connectionManager.Disconnect(reason);

                _isRunning = false;
                Log("VPN client disconnected");
            }
            catch (Exception ex)
            {
                Log($"Error during disconnect: {ex.Message}");
            }
        }

        /// <summary>
        /// Start VPN tunnel
        /// </summary>
        public void StartTunnel()
        {
            if (!_connectionManager.IsConnected)
            {
                Log("Cannot start tunnel: Not connected to server");
                return;
            }

            _tunnelManager.StartTunnel();
        }

        /// <summary>
        /// Stop VPN tunnel
        /// </summary>
        public void StopTunnel()
        {
            _tunnelManager.StopTunnel();
        }

        /// <summary>
        /// Send data through VPN
        /// </summary>
        public void SendData(byte[] data)
        {
            if (!_tunnelManager.IsTunnelActive)
            {
                Log("Cannot send data: Tunnel is not active");
                return;
            }

            _tunnelManager.SendData(data);
        }

        /// <summary>
        /// Handle connection status changes
        /// </summary>
        private void OnConnectionStatusChanged(object sender, ConnectionStatus status)
        {
            ConnectionStatusChanged?.Invoke(this, status);
        }

        /// <summary>
        /// Handle tunnel status changes
        /// </summary>
        private void OnTunnelStatusChanged(object sender, TunnelStatus status)
        {
            TunnelStatusChanged?.Invoke(this, status);
        }

        /// <summary>
        /// Handle log messages
        /// </summary>
        private void OnLogMessage(object sender, string message)
        {
            LogMessage?.Invoke(this, message);
        }

        /// <summary>
        /// Log message
        /// </summary>
        private void Log(string message)
        {
            Console.WriteLine($"[VPN Client] {DateTime.Now:HH:mm:ss} {message}");
            LogMessage?.Invoke(this, message);
        }

        /// <summary>
        /// Display client information
        /// </summary>
        public void DisplayInfo()
        {
            var connectionStats = _connectionManager.IsConnected ? "Connected" : "Disconnected";
            var tunnelStats = _tunnelManager.IsTunnelActive ? "Active" : "Inactive";
            var proxyStats = _localProxy.IsRunning ? "Running" : "Stopped";

            var (bytesSent, bytesReceived, packetsSent, packetsReceived) = _tunnelManager.GetStatistics();
            var (totalConnections, activeConnections) = _localProxy.GetStatistics();

            Console.WriteLine("\n=== VPN Client Information ===");
            Console.WriteLine($"Connection: {connectionStats}");
            Console.WriteLine($"Tunnel: {tunnelStats}");
            Console.WriteLine($"Local Proxy: {proxyStats} (Port: {_config.LocalProxyPort})");
            Console.WriteLine($"Session ID: {_connectionManager.SessionId}");
            Console.WriteLine($"\nTunnel Statistics:");
            Console.WriteLine($"  Bytes Sent: {bytesSent:N0}");
            Console.WriteLine($"  Bytes Received: {bytesReceived:N0}");
            Console.WriteLine($"  Packets Sent: {packetsSent:N0}");
            Console.WriteLine($"  Packets Received: {packetsReceived:N0}");
            Console.WriteLine($"\nProxy Statistics:");
            Console.WriteLine($"  Total Connections: {totalConnections}");
            Console.WriteLine($"  Active Connections: {activeConnections}");
            Console.WriteLine("==============================\n");
        }

        /// <summary>
        /// Get connection manager
        /// </summary>
        public ConnectionManager GetConnectionManager() => _connectionManager;

        /// <summary>
        /// Get tunnel manager
        /// </summary>
        public TunnelManager GetTunnelManager() => _tunnelManager;

        /// <summary>
        /// Get local proxy
        /// </summary>
        public LocalProxy GetLocalProxy() => _localProxy;

        /// <summary>
        /// Check if client is running
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Check if connected to server
        /// </summary>
        public bool IsConnected => _connectionManager.IsConnected;

        /// <summary>
        /// Check if tunnel is active
        /// </summary>
        public bool IsTunnelActive => _tunnelManager.IsTunnelActive;

        public void Dispose()
        {
            Disconnect();
            _connectionManager?.Dispose();
            _tunnelManager?.Dispose();
            _localProxy?.Dispose();
        }
    }
}