using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using VPN.Core.Enums;

namespace VPN.Client
{
    /// <summary>
    /// Manages encrypted VPN tunnel
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

        // Statistics
        private long _bytesSent = 0;
        private long _bytesReceived = 0;
        private int _packetsSent = 0;
        private int _packetsReceived = 0;

        // Events
        public event EventHandler<TunnelStatus> TunnelStatusChanged;
        public event EventHandler<string> LogMessage;

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

                UpdateTunnelStatus(TunnelStatus.Inactive);
                Log("VPN tunnel stopped");
            }
            catch (Exception ex)
            {
                Log($"Error stopping tunnel: {ex.Message}");
            }
        }

        /// <summary>
        /// Main tunnel loop
        /// </summary>
        private async void TunnelLoop()
        {
            try
            {
                while (_isTunnelActive)
                {
                    // Process outgoing queue
                    await ProcessOutgoingQueue();

                    // Process incoming queue
                    await ProcessIncomingQueue();

                    await Task.Delay(10); // Prevent CPU spinning
                }
            }
            catch (Exception ex)
            {
                Log($"Tunnel loop error: {ex.Message}");
                StopTunnel();
            }
        }

        /// <summary>
        /// Process outgoing data queue
        /// </summary>
        private async Task ProcessOutgoingQueue()
        {
            try
            {
                // Process up to 10 packets per cycle
                for (int i = 0; i < 10 && _outgoingQueue.TryDequeue(out byte[] data); i++)
                {
                    if (data != null && data.Length > 0)
                    {
                        await _connectionManager.SendDataAsync(data);

                        // Update statistics
                        _bytesSent += data.Length;
                        _packetsSent++;

                        Log($"Sent {data.Length} bytes through tunnel");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing outgoing queue: {ex.Message}");
            }
        }

        /// <summary>
        /// Process incoming data queue
        /// </summary>
        private async Task ProcessIncomingQueue()
        {
            try
            {
                // Process up to 10 packets per cycle
                for (int i = 0; i < 10 && _incomingQueue.TryDequeue(out byte[] data); i++)
                {
                    if (data != null && data.Length > 0)
                    {
                        // Here you would typically forward this to the local application
                        // For now, just log it
                        Log($"Received {data.Length} bytes through tunnel");

                        // Update statistics
                        _bytesReceived += data.Length;
                        _packetsReceived++;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing incoming queue: {ex.Message}");
            }
        }

        /// <summary>
        /// Send data through tunnel
        /// </summary>
        public void SendData(byte[] data)
        {
            if (!_isTunnelActive)
            {
                Log("Cannot send data: Tunnel is not active");
                return;
            }

            if (data == null || data.Length == 0)
                return;

            try
            {
                _outgoingQueue.Enqueue(data);
                Log($"Queued {data.Length} bytes for sending");
            }
            catch (Exception ex)
            {
                Log($"Error queueing data: {ex.Message}");
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

        /// <summary>
        /// Get tunnel statistics
        /// </summary>
        public (long bytesSent, long bytesReceived, int packetsSent, int packetsReceived) GetStatistics()
        {
            return (_bytesSent, _bytesReceived, _packetsSent, _packetsReceived);
        }

        /// <summary>
        /// Reset statistics
        /// </summary>
        public void ResetStatistics()
        {
            _bytesSent = 0;
            _bytesReceived = 0;
            _packetsSent = 0;
            _packetsReceived = 0;
        }

        /// <summary>
        /// Check if tunnel is active
        /// </summary>
        public bool IsTunnelActive => _isTunnelActive;

        /// <summary>
        /// Get tunnel status
        /// </summary>
        public TunnelStatus Status => _isTunnelActive ? TunnelStatus.Active : TunnelStatus.Inactive;

        public void Dispose()
        {
            StopTunnel();
            _cancellationTokenSource?.Dispose();
        }
    }

    /// <summary>
    /// Tunnel status enum
    /// </summary>
    public enum TunnelStatus
    {
        Inactive,
        Starting,
        Active,
        Stopping,
        Error
    }
}