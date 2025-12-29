using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VPN.Client
{
    /// <summary>
    /// Local SOCKS proxy for intercepting application traffic
    /// </summary>
    public class LocalProxy : IDisposable
    {
        private readonly TunnelManager _tunnelManager;
        private readonly ClientConfiguration _config;

        private TcpListener _tcpListener;
        private bool _isRunning = false;
        private Thread _listenerThread;
        private CancellationTokenSource _cancellationTokenSource;

        // Statistics
        private int _totalConnections = 0;
        private int _activeConnections = 0;

        public LocalProxy(TunnelManager tunnelManager, ClientConfiguration config)
        {
            _tunnelManager = tunnelManager;
            _config = config;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Start local proxy
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                Log("Local proxy is already running");
                return;
            }

            if (!_config.EnableLocalProxy)
            {
                Log("Local proxy is disabled in configuration");
                return;
            }

            try
            {
                _tcpListener = new TcpListener(IPAddress.Loopback, _config.LocalProxyPort);
                _tcpListener.Start();

                _isRunning = true;

                _listenerThread = new Thread(ListenForClients);
                _listenerThread.IsBackground = true;
                _listenerThread.Start();

                Log($"Local SOCKS proxy started on port {_config.LocalProxyPort}");
            }
            catch (Exception ex)
            {
                Log($"Failed to start local proxy: {ex.Message}");
                Stop();
            }
        }

        /// <summary>
        /// Stop local proxy
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                Log("Stopping local proxy...");
                _isRunning = false;
                _cancellationTokenSource.Cancel();

                _tcpListener?.Stop();
                _listenerThread?.Join(1000);

                Log("Local proxy stopped");
            }
            catch (Exception ex)
            {
                Log($"Error stopping local proxy: {ex.Message}");
            }
        }

        /// <summary>
        /// Listen for client connections
        /// </summary>
        private void ListenForClients()
        {
            try
            {
                while (_isRunning)
                {
                    if (_tcpListener.Pending())
                    {
                        TcpClient client = _tcpListener.AcceptTcpClient();

                        // Handle client in separate thread
                        Thread clientThread = new Thread(() => HandleClient(client));
                        clientThread.IsBackground = true;
                        clientThread.Start();

                        _totalConnections++;
                        _activeConnections++;

                        Log($"New proxy client connected. Active: {_activeConnections}, Total: {_totalConnections}");
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                Log($"Listener error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle proxy client
        /// </summary>
        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    // Simple SOCKS5 implementation (for demo)
                    // Real SOCKS5 would have proper authentication and negotiation

                    // Read client request
                    byte[] buffer = new byte[4096];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        // Log request
                        string requestHex = BitConverter.ToString(buffer, 0, Math.Min(bytesRead, 32));
                        Log($"Proxy request (first 32 bytes): {requestHex}");

                        // Forward through tunnel if active
                        if (_tunnelManager.IsTunnelActive)
                        {
                            byte[] requestData = new byte[bytesRead];
                            Buffer.BlockCopy(buffer, 0, requestData, 0, bytesRead);

                            _tunnelManager.SendData(requestData);
                            Log($"Forwarded {bytesRead} bytes through tunnel");

                            // For demo, send a simple response
                            string response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\nVPN Tunnel Working!\r\n";
                            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                            stream.Write(responseBytes, 0, responseBytes.Length);
                        }
                        else
                        {
                            // Tunnel not active - send error
                            string error = "HTTP/1.1 503 Service Unavailable\r\n\r\nVPN tunnel is not active\r\n";
                            byte[] errorBytes = Encoding.UTF8.GetBytes(error);
                            stream.Write(errorBytes, 0, errorBytes.Length);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error handling proxy client: {ex.Message}");
            }
            finally
            {
                _activeConnections--;
                Log($"Proxy client disconnected. Active: {_activeConnections}");
            }
        }

        /// <summary>
        /// Log message
        /// </summary>
        private void Log(string message)
        {
            Console.WriteLine($"[Proxy] {DateTime.Now:HH:mm:ss} {message}");
        }

        /// <summary>
        /// Get proxy statistics
        /// </summary>
        public (int totalConnections, int activeConnections) GetStatistics()
        {
            return (_totalConnections, _activeConnections);
        }

        /// <summary>
        /// Check if proxy is running
        /// </summary>
        public bool IsRunning => _isRunning;

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }
}