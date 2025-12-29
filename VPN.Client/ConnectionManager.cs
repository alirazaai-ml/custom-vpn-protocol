using System;
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

namespace VPN.Client
{
    /// <summary>
    /// Manages connection to VPN server
    /// </summary>
    public class ConnectionManager : IDisposable
    {
        private readonly ClientConfiguration _config;
        private readonly CryptoManager _cryptoManager;

        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private Thread _receiveThread;
        private CancellationTokenSource _cancellationTokenSource;

        private bool _isConnected = false;
        private string _sessionId = string.Empty;
        private int _sessionNumber = 0;
        private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;

        // Events
        public event EventHandler<ConnectionStatus> ConnectionStatusChanged;
        public event EventHandler<string> LogMessage;
        public event EventHandler<byte[]> DataReceived;

        public ConnectionManager(ClientConfiguration config)
        {
            _config = config;
            _cryptoManager = new CryptoManager();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Connect to VPN server
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            if (_isConnected)
            {
                Log("Already connected to server");
                return true;
            }

            try
            {
                UpdateConnectionStatus(ConnectionStatus.Connecting);
                Log($"Connecting to server {_config.ServerIp}:{_config.ServerPort}...");

                // Create TCP client with timeout
                _tcpClient = new TcpClient();
                var connectTask = _tcpClient.ConnectAsync(_config.ServerIp, _config.ServerPort);
                var timeoutTask = Task.Delay(_config.ConnectionTimeout);

                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    throw new VpnException("Connection timeout", 1001);
                }

                await connectTask; // Ensure connection completed

                _networkStream = _tcpClient.GetStream();

                // Perform handshake
                bool handshakeSuccess = await PerformHandshake();
                if (!handshakeSuccess)
                {
                    throw new VpnException("Handshake failed", 1002);
                }

                // Perform authentication if needed
                if (!string.IsNullOrEmpty(_config.Password))
                {
                    bool authSuccess = await PerformAuthentication();
                    if (!authSuccess)
                    {
                        throw new VpnException("Authentication failed", 1003);
                    }
                }

                // Start receive thread
                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();

                _isConnected = true;
                UpdateConnectionStatus(ConnectionStatus.Connected);
                Log($"Successfully connected to server. Session: {_sessionId}");

                // Start keep-alive thread
                StartKeepAlive();

                return true;
            }
            catch (Exception ex)
            {
                Log($"Connection failed: {ex.Message}");
                UpdateConnectionStatus(ConnectionStatus.Error);
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// Disconnect from server
        /// </summary>
        public void Disconnect(string reason = "Client requested disconnect")
        {
            if (!_isConnected && _connectionStatus == ConnectionStatus.Disconnected)
                return;

            try
            {
                UpdateConnectionStatus(ConnectionStatus.Disconnecting);
                Log($"Disconnecting from server: {reason}");

                // Send disconnect packet if still connected
                if (_tcpClient?.Connected == true)
                {
                    var disconnectPacket = PacketBuilder.CreateDisconnectPacket(_sessionNumber, reason);
                    SendPacket(disconnectPacket).Wait(1000);
                }

                // Cleanup
                _cancellationTokenSource?.Cancel();

                _receiveThread?.Join(1000);
                _networkStream?.Close();
                _tcpClient?.Close();

                _cryptoManager?.Dispose();

                _isConnected = false;
                _sessionId = string.Empty;
                _sessionNumber = 0;

                UpdateConnectionStatus(ConnectionStatus.Disconnected);
                Log("Disconnected from server");
            }
            catch (Exception ex)
            {
                Log($"Error during disconnect: {ex.Message}");
            }
        }

        /// <summary>
        /// Perform handshake with server
        /// </summary>
        private async Task<bool> PerformHandshake()
        {
            try
            {
                Log("Performing handshake with server...");

                // Create handshake request
                var handshakeRequest = new HandshakeRequest
                {
                    ClientId = _config.ClientId,
                    Username = _config.Username,
                    SupportedEncryption = _config.EnableEncryption ?
                        new[] { "AES256", "AES128" } : new[] { "None" }
                };

                // Send handshake request
                var requestPacket = PacketBuilder.CreateHandshakeRequest(handshakeRequest);
                await SendPacket(requestPacket);

                // Receive handshake response
                byte[] responseData = await ReceivePacketAsync();
                var responsePacket = PacketSerializer.Deserialize(responseData);

                if (responsePacket.Type != PacketType.HandshakeResponse)
                {
                    throw new VpnException("Invalid handshake response", 1004);
                }

                // Parse response
                var handshakeResponse = PacketSerializer.DeserializeHandshakeResponse(responsePacket.Payload);

                if (handshakeResponse.Status != "success")
                {
                    throw new VpnException($"Handshake failed: {handshakeResponse.Message}", handshakeResponse.ErrorCode);
                }

                _sessionId = handshakeResponse.SessionId;
                _sessionNumber = responsePacket.SessionId;

                Log($"Handshake successful. Session ID: {_sessionId}");

                // Perform key exchange if encryption is enabled
                if (_config.EnableEncryption && handshakeResponse.SelectedEncryption != EncryptionType.None)
                {
                    bool keyExchangeSuccess = await PerformKeyExchange();
                    if (!keyExchangeSuccess)
                    {
                        throw new VpnException("Key exchange failed", 1005);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log($"Handshake error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Perform key exchange with server
        /// </summary>
        private async Task<bool> PerformKeyExchange()
        {
            try
            {
                Log("Performing key exchange...");

                // Send our public key
                byte[] publicKey = _cryptoManager.GetPublicKey();
                var keyExchangePacket = PacketBuilder.CreateDataPacket(_sessionNumber, publicKey);
                await SendPacket(keyExchangePacket);

                // Receive server's public key
                byte[] serverKeyData = await ReceivePacketAsync();
                var serverKeyPacket = PacketSerializer.Deserialize(serverKeyData);

                if (serverKeyPacket.Type != PacketType.Data)
                {
                    throw new VpnException("Invalid key exchange response", 1006);
                }

                // Derive session key
                var (sessionKey, iv) = _cryptoManager.PerformKeyExchange(serverKeyPacket.Payload);

                // Initialize crypto manager with session key
                _cryptoManager.Initialize(sessionKey, iv);

                Log("Key exchange completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Key exchange error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Perform authentication with server
        /// </summary>
        private async Task<bool> PerformAuthentication()
        {
            try
            {
                Log("Performing authentication...");

                var authData = new
                {
                    username = _config.Username,
                    password = _config.Password,
                    timestamp = DateTime.UtcNow
                };

                string json = JsonSerializer.Serialize(authData);
                byte[] authBytes = Encoding.UTF8.GetBytes(json);

                var authPacket = PacketBuilder.CreateDataPacket(_sessionNumber, authBytes);

                // Encrypt if enabled
                VpnPacket packetToSend = _config.EnableEncryption ?
                    _cryptoManager.EncryptPacket(authPacket) : authPacket;

                await SendPacket(packetToSend);

                // Wait for authentication response
                byte[] responseData = await ReceivePacketAsync(5000);
                var responsePacket = PacketSerializer.Deserialize(responseData);

                // Decrypt if needed
                if (_config.EnableEncryption && responsePacket.Type == PacketType.Data)
                {
                    responsePacket = _cryptoManager.DecryptPacket(responsePacket);
                }

                string response = Encoding.UTF8.GetString(responsePacket.Payload);
                var authResponse = JsonSerializer.Deserialize<dynamic>(response);

                string status = authResponse?.GetProperty("status").GetString() ?? "failed";

                if (status == "authenticated")
                {
                    Log("Authentication successful");
                    return true;
                }
                else
                {
                    Log("Authentication failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Authentication error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send data through VPN tunnel
        /// </summary>
        public async Task SendDataAsync(byte[] data)
        {
            if (!_isConnected)
                throw new VpnException("Not connected to server", 1007);

            try
            {
                var dataPacket = PacketBuilder.CreateDataPacket(_sessionNumber, data);

                // Encrypt if enabled
                VpnPacket packetToSend = _config.EnableEncryption ?
                    _cryptoManager.EncryptPacket(dataPacket) : dataPacket;

                await SendPacket(packetToSend);
            }
            catch (Exception ex)
            {
                Log($"Error sending data: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Receive data from VPN tunnel
        /// </summary>
        public async Task<byte[]> ReceiveDataAsync(int timeout = 5000)
        {
            try
            {
                byte[] packetData = await ReceivePacketAsync(timeout);
                var packet = PacketSerializer.Deserialize(packetData);

                // Decrypt if needed
                if (_config.EnableEncryption && packet.Type == PacketType.Data)
                {
                    packet = _cryptoManager.DecryptPacket(packet);
                }

                return packet.Payload;
            }
            catch (Exception ex)
            {
                Log($"Error receiving data: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Send raw packet
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
        /// Receive raw packet
        /// </summary>
        private async Task<byte[]> ReceivePacketAsync(int timeout = 5000)
        {
            try
            {
                byte[] buffer = new byte[_config.BufferSize];
                using var timeoutCts = new CancellationTokenSource(timeout);

                int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, timeoutCts.Token);

                if (bytesRead == 0)
                    throw new VpnException("Connection closed by server", 1008);

                byte[] packetData = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, packetData, 0, bytesRead);

                return packetData;
            }
            catch (OperationCanceledException)
            {
                throw new VpnException("Receive timeout", 1009);
            }
            catch (Exception ex)
            {
                Log($"Error receiving packet: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Main receive loop
        /// </summary>
        private async void ReceiveLoop()
        {
            try
            {
                byte[] buffer = new byte[_config.BufferSize];

                while (_isConnected && _tcpClient.Connected)
                {
                    if (_networkStream.DataAvailable)
                    {
                        int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token);

                        if (bytesRead > 0)
                        {
                            byte[] packetData = new byte[bytesRead];
                            Buffer.BlockCopy(buffer, 0, packetData, 0, bytesRead);

                            // Process packet
                            await ProcessReceivedPacket(packetData);
                        }
                    }
                    else
                    {
                        await Task.Delay(10);
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
                if (_isConnected)
                {
                    Log("Receive loop terminated. Disconnecting...");
                    Disconnect("Connection lost");
                }
            }
        }

        /// <summary>
        /// Process received packet
        /// </summary>
        private async Task ProcessReceivedPacket(byte[] packetData)
        {
            try
            {
                var packet = PacketSerializer.Deserialize(packetData);

                switch (packet.Type)
                {
                    case PacketType.Data:
                        await ProcessDataPacket(packet);
                        break;

                    case PacketType.KeepAlive:
                        await ProcessKeepAlive(packet);
                        break;

                    case PacketType.Disconnect:
                        await ProcessDisconnect(packet);
                        break;

                    case PacketType.Error:
                        await ProcessErrorPacket(packet);
                        break;

                    default:
                        Log($"Unknown packet type received: {packet.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing packet: {ex.Message}");
            }
        }

        /// <summary>
        /// Process data packet
        /// </summary>
        private async Task ProcessDataPacket(VpnPacket packet)
        {
            try
            {
                // Decrypt if needed
                VpnPacket decryptedPacket = _config.EnableEncryption ?
                    _cryptoManager.DecryptPacket(packet) : packet;

                // Raise data received event
                DataReceived?.Invoke(this, decryptedPacket.Payload);
            }
            catch (Exception ex)
            {
                Log($"Error processing data packet: {ex.Message}");
            }
        }

        /// <summary>
        /// Process keep-alive packet
        /// </summary>
        private async Task ProcessKeepAlive(VpnPacket packet)
        {
            // Send keep-alive response
            var responsePacket = PacketBuilder.CreateKeepAlivePacket(packet.SessionId);
            await SendPacket(responsePacket);
        }

        /// <summary>
        /// Process disconnect packet
        /// </summary>
        private async Task ProcessDisconnect(VpnPacket packet)
        {
            string reason = packet.Payload.Length > 0 ?
                Encoding.UTF8.GetString(packet.Payload) : "Server requested disconnect";

            Log($"Server disconnect: {reason}");
            Disconnect(reason);
        }

        /// <summary>
        /// Process error packet
        /// </summary>
        private async Task ProcessErrorPacket(VpnPacket packet)
        {
            string errorJson = Encoding.UTF8.GetString(packet.Payload);
            var errorInfo = JsonSerializer.Deserialize<dynamic>(errorJson);

            int errorCode = errorInfo?.GetProperty("code").GetInt32() ?? 0;
            string message = errorInfo?.GetProperty("message").GetString() ?? "Unknown error";

            Log($"Server error [{errorCode}]: {message}");
        }

        /// <summary>
        /// Start keep-alive thread
        /// </summary>
        private void StartKeepAlive()
        {
            Thread keepAliveThread = new Thread(async () =>
            {
                while (_isConnected)
                {
                    try
                    {
                        await Task.Delay(_config.KeepAliveInterval);

                        if (_isConnected)
                        {
                            var keepAlivePacket = PacketBuilder.CreateKeepAlivePacket(_sessionNumber);
                            await SendPacket(keepAlivePacket);
                        }
                    }
                    catch
                    {
                        // Ignore keep-alive errors
                    }
                }
            });

            keepAliveThread.IsBackground = true;
            keepAliveThread.Start();
        }

        /// <summary>
        /// Update connection status
        /// </summary>
        private void UpdateConnectionStatus(ConnectionStatus status)
        {
            _connectionStatus = status;
            ConnectionStatusChanged?.Invoke(this, status);
        }

        /// <summary>
        /// Log message
        /// </summary>
        private void Log(string message)
        {
            Console.WriteLine($"[Connection] {DateTime.Now:HH:mm:ss} {message}");
            LogMessage?.Invoke(this, message);
        }

        /// <summary>
        /// Check if connected
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Get connection status
        /// </summary>
        public ConnectionStatus Status => _connectionStatus;

        /// <summary>
        /// Get session ID
        /// </summary>
        public string SessionId => _sessionId;

        public void Dispose()
        {
            Disconnect();
            _cancellationTokenSource?.Dispose();
            _cryptoManager?.Dispose();
        }
    }
}