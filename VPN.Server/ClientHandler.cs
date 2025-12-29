using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VPN.Core.Enums;
using VPN.Core.Exceptions;
using VPN.Core.Models;
using VPN.Core.Protocol;
using VPN.Core.Security;

namespace VPN.Server
{
    /// <summary>
    /// Handles individual client connections
    /// </summary>
    public class ClientHandler : IDisposable
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _networkStream;
        private readonly SessionManager _sessionManager;
        private readonly CryptoManager _cryptoManager;
        private readonly PacketForwarder _packetForwarder;
        private readonly ServerConfiguration _config;

        private Session _session;
        private bool _isRunning = false;
        private Thread _receiveThread;
        private CancellationTokenSource _cancellationTokenSource;

        public ClientHandler(TcpClient tcpClient, SessionManager sessionManager,
                           PacketForwarder packetForwarder, ServerConfiguration config)
        {
            _tcpClient = tcpClient;
            _networkStream = tcpClient.GetStream();
            _sessionManager = sessionManager;
            _packetForwarder = packetForwarder;
            _config = config;
            _cryptoManager = new CryptoManager();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Start handling client
        /// </summary>
        public void Start()
        {
            _isRunning = true;
            _receiveThread = new Thread(ReceiveLoop);
            _receiveThread.IsBackground = true;
            _receiveThread.Start();

            Log($"Client handler started for {GetClientEndpoint()}");
        }

        /// <summary>
        /// Stop handling client
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _cancellationTokenSource.Cancel();

            try
            {
                _receiveThread?.Join(1000);
                _networkStream?.Close();
                _tcpClient?.Close();

                if (_session != null)
                {
                    _sessionManager.RemoveSession(_session.SessionId);
                }
            }
            catch (Exception ex)
            {
                Log($"Error stopping client handler: {ex.Message}");
            }

            Log($"Client handler stopped for {GetClientEndpoint()}");
        }

        /// <summary>
        /// Main receive loop
        /// </summary>
        private async void ReceiveLoop()
        {
            try
            {
                byte[] buffer = new byte[_config.BufferSize];

                while (_isRunning && _tcpClient.Connected)
                {
                    // Check for data available
                    if (_networkStream.DataAvailable)
                    {
                        int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token);

                        if (bytesRead > 0)
                        {
                            byte[] receivedData = new byte[bytesRead];
                            Buffer.BlockCopy(buffer, 0, receivedData, 0, bytesRead);

                            await ProcessReceivedData(receivedData);
                        }
                    }
                    else
                    {
                        await Task.Delay(10); // Small delay to prevent CPU spinning
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                Log($"Receive loop error: {ex.Message}");
            }
            finally
            {
                Stop();
            }
        }

        /// <summary>
        /// Process received data from client
        /// </summary>
        private async Task ProcessReceivedData(byte[] data)
        {
            try
            {
                // Deserialize packet
                VpnPacket packet = PacketSerializer.Deserialize(data);

                // Update session activity
                if (_session != null)
                {
                    _sessionManager.UpdateSessionActivity(_session.SessionId);
                }

                // Process based on packet type
                switch (packet.Type)
                {
                    case PacketType.HandshakeRequest:
                        await ProcessHandshakeRequest(packet);
                        break;

                    case PacketType.Authentication:
                        await ProcessAuthentication(packet);
                        break;

                    case PacketType.Data:
                        await ProcessDataPacket(packet);
                        break;

                    case PacketType.KeepAlive:
                        await ProcessKeepAlive(packet);
                        break;

                    case PacketType.Disconnect:
                        await ProcessDisconnect(packet);
                        break;

                    default:
                        Log($"Unknown packet type: {packet.Type}");
                        break;
                }
            }
            catch (VpnException ex)
            {
                Log($"VPN error processing data: {ex.Message}");
                await SendErrorPacket(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                Log($"Error processing data: {ex.Message}");
                await SendErrorPacket(1000, "Internal server error");
            }
        }

        /// <summary>
        /// Process handshake request
        /// </summary>
        private async Task ProcessHandshakeRequest(VpnPacket packet)
        {
            try
            {
                // Deserialize handshake request
                HandshakeRequest request = JsonSerializer.Deserialize<HandshakeRequest>(
                    Encoding.UTF8.GetString(packet.Payload));

                if (request == null)
                    throw new VpnException("Invalid handshake request");

                Log($"Handshake request from {request.ClientId} ({request.Username})");

                // Create new session
                var endpoint = (IPEndPoint)_tcpClient.Client.RemoteEndPoint!;
                _session = _sessionManager.CreateSession(request.ClientId, endpoint);

                // Send handshake response
                var response = new HandshakeResponse
                {
                    Status = "success",
                    Message = "Handshake accepted",
                    SessionId = _session.SessionId,
                    SelectedEncryption = _config.EnableEncryption ?
                        EncryptionType.AES256 : EncryptionType.None,
                    KeepAliveInterval = _config.KeepAliveInterval
                };

                var responsePacket = PacketBuilder.CreateHandshakeResponse(response, packet.SessionId);
                await SendPacket(responsePacket);

                Log($"Handshake completed for session {_session.SessionId}");
            }
            catch (Exception ex)
            {
                throw new VpnException($"Handshake failed: {ex.Message}", 1001);
            }
        }

        /// <summary>
        /// Process authentication
        /// </summary>
        private async Task ProcessAuthentication(VpnPacket packet)
        {
            if (_session == null)
                throw new VpnException("No active session for authentication");

            try
            {
                // For demo: simple password check
                // In production, use proper authentication
                string authData = Encoding.UTF8.GetString(packet.Payload);
                var authInfo = JsonSerializer.Deserialize<dynamic>(authData);

                string password = authInfo?.GetProperty("password").GetString() ?? "";

                if (_config.RequireAuthentication && password != _config.AdminPassword)
                {
                    throw new VpnException("Authentication failed", 1002);
                }

                // Update session status
                _sessionManager.UpdateSessionStatus(_session.SessionId, ConnectionStatus.Connected);

                // Send authentication success
                var successResponse = new { status = "authenticated", timestamp = DateTime.UtcNow };
                byte[] responseData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(successResponse));

                var responsePacket = PacketBuilder.CreateDataPacket(packet.SessionId, responseData);
                await SendPacket(responsePacket);

                Log($"Client authenticated for session {_session.SessionId}");
            }
            catch (Exception ex)
            {
                throw new VpnException($"Authentication failed: {ex.Message}", 1002);
            }
        }

        /// <summary>
        /// Process data packet
        /// </summary>
        private async Task ProcessDataPacket(VpnPacket packet)
        {
            if (_session == null)
                throw new VpnException("No active session for data");

            try
            {
                // Decrypt data if encryption is enabled
                byte[] decryptedData;

                if (_config.EnableEncryption && _cryptoManager != null)
                {
                    var decryptedPacket = _cryptoManager.DecryptPacket(packet);
                    decryptedData = decryptedPacket.Payload;
                }
                else
                {
                    decryptedData = packet.Payload;
                }

                // Forward to internet
                await _packetForwarder.ForwardToInternet(_session.SessionId, decryptedData);

                // Get response from internet (simulated)
                byte[] responseData = await _packetForwarder.ReceiveFromInternet(_session.SessionId);

                if (responseData.Length > 0)
                {
                    // Encrypt response if needed
                    VpnPacket responsePacket;

                    if (_config.EnableEncryption && _cryptoManager != null)
                    {
                        var dataPacket = PacketBuilder.CreateDataPacket(packet.SessionId, responseData);
                        responsePacket = _cryptoManager.EncryptPacket(dataPacket);
                    }
                    else
                    {
                        responsePacket = PacketBuilder.CreateDataPacket(packet.SessionId, responseData);
                    }

                    await SendPacket(responsePacket);
                }
            }
            catch (Exception ex)
            {
                throw new VpnException($"Data processing failed: {ex.Message}", 1004);
            }
        }

        /// <summary>
        /// Process keep-alive
        /// </summary>
        private async Task ProcessKeepAlive(VpnPacket packet)
        {
            if (_session != null)
            {
                _sessionManager.UpdateSessionActivity(_session.SessionId);

                // Send keep-alive response
                var responsePacket = PacketBuilder.CreateKeepAlivePacket(packet.SessionId);
                await SendPacket(responsePacket);
            }
        }

        /// <summary>
        /// Process disconnect
        /// </summary>
        private async Task ProcessDisconnect(VpnPacket packet)
        {
            string reason = packet.Payload.Length > 0 ?
                Encoding.UTF8.GetString(packet.Payload) : "Client requested disconnect";

            Log($"Client disconnect: {reason}");

            // Send acknowledgment
            var ackPacket = PacketBuilder.CreateDataPacket(packet.SessionId,
                Encoding.UTF8.GetBytes("DISCONNECT_ACK"));
            await SendPacket(ackPacket);

            Stop();
        }

        /// <summary>
        /// Send packet to client
        /// </summary>
        private async Task SendPacket(VpnPacket packet)
        {
            try
            {
                byte[] packetData = PacketSerializer.Serialize(packet);
                await _networkStream.WriteAsync(packetData, 0, packetData.Length, _cancellationTokenSource.Token);
                await _networkStream.FlushAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Log($"Error sending packet: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Send error packet
        /// </summary>
        private async Task SendErrorPacket(int errorCode, string message)
        {
            try
            {
                var errorPacket = PacketBuilder.CreateErrorPacket(
                    _session != null ? _session.SessionId : 0,
                    errorCode,
                    message);

                await SendPacket(errorPacket);
            }
            catch (Exception ex)
            {
                Log($"Error sending error packet: {ex.Message}");
            }
        }

        /// <summary>
        /// Get client endpoint string
        /// </summary>
        private string GetClientEndpoint()
        {
            try
            {
                return _tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Check if handler is running
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Get client session
        /// </summary>
        public Session GetSession() => _session;

        private void Log(string message)
        {
            Console.WriteLine($"[ClientHandler] {DateTime.Now:HH:mm:ss} {GetClientEndpoint()} - {message}");
        }

        public void Dispose()
        {
            Stop();
            _cryptoManager?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}