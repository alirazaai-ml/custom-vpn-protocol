using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using VPN.Core.Enums;
using VPN.Core.Exceptions;
using VPN.Core.Models;
using VPN.Core.Protocol;
using VPN.Core.Security;

namespace VPN.Server
{
    // Event delegates for ClientHandler
    public delegate void ClientIdentifiedEventHandler(object sender, ClientIdentifiedEventArgs e);
    public delegate void ClientDisconnectedEventHandler(object sender, ClientDisconnectedEventArgs e);
    public delegate void ClientLogMessageEventHandler(object sender, ClientLogMessageEventArgs e);
    public delegate void ClientDataTransferEventHandler(object sender, ClientDataTransferEventArgs e);

    // Event args for ClientHandler
    public class ClientIdentifiedEventArgs : EventArgs
    {
        public string ClientId { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class ClientDisconnectedEventArgs : EventArgs
    {
        public string ClientId { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class ClientLogMessageEventArgs : EventArgs
    {
        public string Message { get; set; } = string.Empty;
        public string Level { get; set; } = "INFO"; // "INFO", "WARN", "ERROR"
        public string ClientId { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class ClientDataTransferEventArgs : EventArgs
    {
        public string ClientId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
        public string Direction { get; set; } = "unknown"; // "upload" or "download"
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

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
        private readonly VpnServer _server; // NEW: Reference to VpnServer

        private Session? _session;
        private bool _isRunning = false;
        private Thread? _receiveThread;
        private Thread? _keepAliveThread;
        private CancellationTokenSource _cancellationTokenSource;

        // Client information
        private string _clientId = string.Empty;
        private string _username = string.Empty;
        private string _clientIp = string.Empty;
        private bool _isAuthenticated = false;
        private DateTime _connectedAt;

        // Statistics
        private long _bytesSent = 0;
        private long _bytesReceived = 0;
        private DateTime _lastActivity = DateTime.Now;

        // Events
        public event ClientIdentifiedEventHandler? ClientIdentified;
        public event ClientDisconnectedEventHandler? ClientDisconnected;
        public event ClientLogMessageEventHandler? LogMessage;
        public event ClientDataTransferEventHandler? DataTransferred;

        public ClientHandler(TcpClient tcpClient, SessionManager sessionManager,
                           PacketForwarder packetForwarder, ServerConfiguration config, VpnServer server)
        {
            _tcpClient = tcpClient;
            _networkStream = tcpClient.GetStream();
            _sessionManager = sessionManager;
            _packetForwarder = packetForwarder;
            _config = config;
            _server = server; // NEW: Store VpnServer reference
            _cryptoManager = new CryptoManager();
            _cancellationTokenSource = new CancellationTokenSource();

            // Get client IP
            var endpoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint!;
            _clientIp = endpoint.Address.ToString();
            _connectedAt = DateTime.Now;
        }

        /// <summary>
        /// Start handling client
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;

            // Start receive thread
            _receiveThread = new Thread(ReceiveLoop);
            _receiveThread.IsBackground = true;
            _receiveThread.Start();

            // Start keep-alive thread
            _keepAliveThread = new Thread(KeepAliveLoop);
            _keepAliveThread.IsBackground = true;
            _keepAliveThread.Start();

            OnLogMessage($"Client handler started for {_clientIp}", "INFO");
        }

        /// <summary>
        /// Stop handling client
        /// </summary>
        public void Stop(string reason = "Server requested disconnect")
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellationTokenSource.Cancel();

            try
            {
                // Wait for threads to finish
                _receiveThread?.Join(1000);
                _keepAliveThread?.Join(500);

                // Close network connections
                _networkStream?.Close();
                _tcpClient?.Close();

                // Remove session
                if (_session != null)
                {
                    _sessionManager.RemoveSession(_session.SessionId);
                }

                // Trigger disconnected event
                OnClientDisconnected(reason);
            }
            catch (Exception ex)
            {
                OnLogMessage($"Error stopping client handler: {ex.Message}", "ERROR");
            }
            finally
            {
                OnLogMessage($"Client handler stopped for {_clientIp}. Reason: {reason}", "INFO");
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

                            // Update activity timestamp
                            _lastActivity = DateTime.Now;

                            // Update received bytes
                            _bytesReceived += bytesRead;

                            // Process the data
                            await ProcessReceivedData(receivedData);

                            // Trigger data transfer event
                            OnDataTransferred(bytesRead, 0, "download");
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
                OnLogMessage($"Receive loop error: {ex.Message}", "ERROR");
            }
            finally
            {
                if (_isRunning)
                {
                    Stop("Connection lost");
                }
            }
        }

        /// <summary>
        /// Keep-alive monitoring loop
        /// </summary>
        private async void KeepAliveLoop()
        {
            while (_isRunning)
            {
                try
                {
                    // Check if client is still active
                    if ((DateTime.Now - _lastActivity).TotalMilliseconds > _config.SessionTimeout)
                    {
                        OnLogMessage($"Client timeout - no activity for {_config.SessionTimeout}ms", "WARN");
                        Stop("Connection timeout");
                        break;
                    }

                    // Send keep-alive if client is authenticated
                    if (_isAuthenticated && _session != null)
                    {
                        try
                        {
                            var keepAlivePacket = PacketBuilder.CreateKeepAlivePacket(_session.SessionIdHash);
                            await SendPacket(keepAlivePacket);
                        }
                        catch
                        {
                            // Ignore keep-alive errors, connection will timeout
                        }
                    }

                    // Wait for next check
                    await Task.Delay(_config.KeepAliveInterval);
                }
                catch (Exception ex)
                {
                    OnLogMessage($"Keep-alive loop error: {ex.Message}", "ERROR");
                    break;
                }
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
                        OnLogMessage($"Unknown packet type: {packet.Type}", "WARN");
                        break;
                }
            }
            catch (VpnException ex)
            {
                OnLogMessage($"VPN error: {ex.Message} (Code: {ex.ErrorCode})", "ERROR");
                await SendErrorPacket(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                OnLogMessage($"Error processing data: {ex.Message}", "ERROR");
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
                OnLogMessage($"📥 Processing handshake request - packet length: {packet.Payload?.Length ?? 0}", "INFO");
                
                // ✅ DEBUG: Log raw packet data
                if (packet.Payload != null && packet.Payload.Length > 0)
                {
                    string payloadPreview = Encoding.UTF8.GetString(packet.Payload).Substring(0, Math.Min(100, packet.Payload.Length));
                    OnLogMessage($"📝 Handshake payload preview: {payloadPreview}...", "DEBUG");
                }

                // Deserialize handshake request
                HandshakeRequest? request = JsonSerializer.Deserialize<HandshakeRequest>(
                    Encoding.UTF8.GetString(packet.Payload));

                if (request == null)
                {
                    OnLogMessage($"❌ Failed to deserialize handshake request", "ERROR");
                    throw new VpnException("Invalid handshake request");
                }

                // Store client info
                _clientId = request.ClientId;
                _username = request.Username;

                OnLogMessage($"✅ Handshake request from {_clientId} ({_username})", "INFO");

                // ✅ DEBUG: Check configuration state
                OnLogMessage($"🔍 RequireApproval setting: {_config.RequireApproval}", "DEBUG");
                OnLogMessage($"🔍 Current approved users count: {_config.ApprovedUsers?.Count ?? 0}", "DEBUG");

                // ? NEW: Check username uniqueness
                if (_config.IsUsernameTaken(_username))
                {
                    // Check if it's the SAME client reconnecting
                    var existingUser = _config.ApprovedUsers.FirstOrDefault(u => 
                        u.Username.Equals(_username, StringComparison.OrdinalIgnoreCase));
                    
                    if (existingUser != null && existingUser.ClientId != _clientId)
                    {
                        // Different client trying to use same username
                        OnLogMessage($"Username '{_username}' already in use by another client", "WARN");
                        throw new VpnException("Username already in use. Please choose another username.", 2001);
                    }
                }

                // ? NEW: Check if user needs approval
                bool userIsApproved = _config.IsUserApproved(_username);
                OnLogMessage($"🔍 User '{_username}' approved status: {userIsApproved}", "DEBUG");

                if (!userIsApproved)
                {
                    // New user - request approval from server dashboard
                    OnLogMessage($"🆕 New user '{_username}' - requesting approval from administrator...", "INFO");
                    
                    try
                    {
                        // ✅ DEBUG: Ensure server reference is valid
                        if (_server == null)
                        {
                            OnLogMessage($"❌ CRITICAL: VpnServer reference is null!", "ERROR");
                            throw new VpnException("Server error - cannot process approval", 5001);
                        }

                        OnLogMessage($"📞 Calling RequestUserApproval for user '{_username}'...", "DEBUG");
                        
                        // Request approval via VpnServer event
                        bool approved = await _server.RequestUserApproval(_username, _clientId, _clientIp);
                        
                        OnLogMessage($"📋 Approval result for '{_username}': {approved}", "INFO");
                        
                        if (!approved)
                        {
                            OnLogMessage($"❌ User '{_username}' rejected by administrator", "WARN");
                            throw new VpnException("User approval denied by server administrator.", 2003);
                        }
                        
                        // Approve and save user
                        _config.ApproveUser(_username, _clientId);
                        OnLogMessage($"✅ User '{_username}' approved and added to approved users list", "INFO");
                    }
                    catch (Exception approvalEx)
                    {
                        OnLogMessage($"❌ Error during approval process: {approvalEx.Message}", "ERROR");
                        OnLogMessage($"📍 Approval exception type: {approvalEx.GetType().Name}", "ERROR");
                        if (approvalEx.InnerException != null)
                        {
                            OnLogMessage($"📍 Inner exception: {approvalEx.InnerException.Message}", "ERROR");
                        }
                        throw; // Re-throw the approval exception
                    }
                }
                else
                {
                    // Returning user - auto-approve
                    _config.UpdateUserConnection(_username);
                    OnLogMessage($"🔄 Returning user '{_username}' auto-approved", "INFO");
                }

                // Create new session
                var endpoint = (IPEndPoint)_tcpClient.Client.RemoteEndPoint!;
                _session = _sessionManager.CreateSession(_clientId, endpoint);

                OnLogMessage($"📋 Session created: {_session.SessionId}", "INFO");

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

                OnLogMessage($"📤 Handshake response sent to client", "INFO");

                // ✅ NEW: Perform key exchange if encryption is enabled
                if (_config.EnableEncryption && response.SelectedEncryption != EncryptionType.None)
                {
                    OnLogMessage("🔐 Waiting for client's public key...", "INFO");
                    // Client will send its public key as next packet
                    // We'll handle it in ProcessDataPacket with special flag
                    _isAuthenticated = false; // Not authenticated yet, waiting for key exchange
                }
                else
                {
                    // No encryption, mark as authenticated immediately
                    _isAuthenticated = true;
                    OnLogMessage("✅ Client authenticated (no encryption)", "INFO");
                }

                // Trigger client identified event
                OnClientIdentified();

                OnLogMessage($"🎉 Handshake completed for session {_session.SessionId}", "INFO");
            }
            catch (JsonException jsonEx)
            {
                OnLogMessage($"❌ JSON deserialization error: {jsonEx.Message}", "ERROR");
                throw new VpnException($"Handshake request format error: {jsonEx.Message}", 1001);
            }
            catch (VpnException)
            {
                // Re-throw VPN exceptions as-is
                throw;
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Unexpected handshake error: {ex.Message}", "ERROR");
                OnLogMessage($"📍 Exception type: {ex.GetType().Name}", "ERROR");
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
                // Decrypt if needed
                string authData;
                if (_config.EnableEncryption && _cryptoManager != null)
                {
                    var decryptedPacket = _cryptoManager.DecryptPacket(packet);
                    authData = Encoding.UTF8.GetString(decryptedPacket.Payload);
                }
                else
                {
                    authData = Encoding.UTF8.GetString(packet.Payload);
                }

                var authInfo = JsonSerializer.Deserialize<dynamic>(authData);

                string username = authInfo?.GetProperty("username").GetString() ?? "";
                string password = authInfo?.GetProperty("password").GetString() ?? "";

                OnLogMessage($"Authentication attempt for user: {username}", "INFO");

                // Verify password
                if (_config.RequireAuthentication && !_config.VerifyPassword(password))
                {
                    OnLogMessage($"Authentication failed for user: {username}", "WARN");
                    throw new VpnException("Authentication failed", 1002);
                }

                // Update session status
                _sessionManager.UpdateSessionStatus(_session.SessionId, ConnectionStatus.Connected);
                _isAuthenticated = true;

                // Send authentication success
                var successResponse = new
                {
                    status = "authenticated",
                    message = "Authentication successful",
                    timestamp = DateTime.UtcNow,
                    sessionId = _session.SessionId
                };

                byte[] responseData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(successResponse));

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

                OnLogMessage($"Client authenticated successfully: {_clientId}", "INFO");
            }
            catch (Exception ex)
            {
                throw new VpnException($"Authentication failed: {ex.Message}", 1002);
            }
        }

        /// <summary>
        /// Process data packet - WITH CONNECTION ID ROUTING
        /// </summary>
        private async Task ProcessDataPacket(VpnPacket packet)
        {
            if (_session == null)
                throw new VpnException("No active session for data");

            // ✅ Handle key exchange if encryption enabled and not yet authenticated
            if (_config.EnableEncryption && !_isAuthenticated)
            {
                try
                {
                    OnLogMessage("Received client public key, performing key exchange...", "INFO");
                    
                    byte[] clientPublicKey = packet.Payload;
                    byte[] sessionKey = _cryptoManager.PerformKeyExchange(clientPublicKey);
                    _cryptoManager.Initialize(sessionKey);
                    
                    // ✅ FIXED: Simple key verification without problematic test
                    string keyFingerprint = BitConverter.ToString(sessionKey.Take(8).ToArray()).Replace("-", "");
                    OnLogMessage($"🔑 Session key established: {keyFingerprint}...", "INFO");
                    
                    OnLogMessage("Key exchange successful, sending server public key...", "INFO");
                    
                    byte[] serverPublicKey = _cryptoManager.GetPublicKey();
                    var keyExchangeResponse = PacketBuilder.CreateDataPacket(packet.SessionId, serverPublicKey);
                    await SendPacket(keyExchangeResponse);
                    
                    _isAuthenticated = true;
                    _sessionManager.UpdateSessionStatus(_session.SessionId, ConnectionStatus.Connected);
                    
                    OnLogMessage("✅ Encryption established, client authenticated", "INFO");
                    return;
                }
                catch (Exception ex)
                {
                    throw new VpnException($"Key exchange failed: {ex.Message}", 1005);
                }
            }

            if (!_isAuthenticated)
                throw new VpnException("Client not authenticated", 1003);

            try
            {
                // Decrypt data if encryption is enabled
                byte[] decryptedData;

                if (_config.EnableEncryption && _cryptoManager != null)
                {
                    var decryptedPacket = _cryptoManager.DecryptPacket(packet);
                    decryptedData = decryptedPacket.Payload;
                    
                    // ✅ Log successful decryption
                    OnLogMessage($"🔓 Decrypted packet: {packet.Payload.Length} → {decryptedData.Length} bytes", "DEBUG");
                }
                else
                {
                    decryptedData = packet.Payload;
                }

                // ✅ NEW: Extract connection ID hash from data (first 4 bytes)
                int connectionIdHash = 0;
                byte[] actualData = decryptedData;
                
                if (decryptedData.Length >= 4)
                {
                    // Extract hash from first 4 bytes
                    connectionIdHash = BitConverter.ToInt32(decryptedData, 0);
                    
                    // Remove hash from data (keep only actual payload)
                    actualData = new byte[decryptedData.Length - 4];
                    Buffer.BlockCopy(decryptedData, 4, actualData, 0, actualData.Length);
                    
                    OnLogMessage($"📋 Extracted connection ID hash: 0x{connectionIdHash:X8}, Data: {actualData.Length} bytes", "DEBUG");
                }

                // Forward to internet
                await _packetForwarder.ForwardToInternet(_session.SessionId, actualData);

                // Get response from internet
                byte[] responseData = await _packetForwarder.ReceiveFromInternet(_session.SessionId);

                if (responseData != null && responseData.Length > 0)
                {
                    // ✅ NEW: Prepend connection ID hash back to response
                    byte[] responseWithHash;
                    
                    if (connectionIdHash != 0)
                    {
                        responseWithHash = new byte[4 + responseData.Length];
                        BitConverter.GetBytes(connectionIdHash).CopyTo(responseWithHash, 0);
                        responseData.CopyTo(responseWithHash, 4);
                        
                        OnLogMessage($"📤 Sending response with hash: 0x{connectionIdHash:X8}, Data: {responseData.Length} bytes", "DEBUG");
                    }
                    else
                    {
                        responseWithHash = responseData;
                    }

                    // Encrypt response if needed
                    VpnPacket responsePacket;

                    if (_config.EnableEncryption && _cryptoManager != null)
                    {
                        var dataPacket = PacketBuilder.CreateDataPacket(packet.SessionId, responseWithHash);
                        responsePacket = _cryptoManager.EncryptPacket(dataPacket);
                        
                        // ✅ Log successful encryption
                        OnLogMessage($"🔒 Encrypted response: {responseWithHash.Length} → {responsePacket.Payload.Length} bytes", "DEBUG");
                    }
                    else
                    {
                        responsePacket = PacketBuilder.CreateDataPacket(packet.SessionId, responseWithHash);
                    }

                    await SendPacket(responsePacket);

                    // Trigger data transfer event for upload
                    OnDataTransferred(0, responseWithHash.Length, "upload");
                    
                    OnLogMessage($"✅ Routed response: {responseData.Length} bytes to connection 0x{connectionIdHash:X8}", "INFO");
                }
                else
                {
                    OnLogMessage("⚠️ No response from internet forwarder", "WARN");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Data processing error: {ex.Message}", "ERROR");
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

                OnLogMessage("Keep-alive received and responded", "DEBUG");
            }
        }

        /// <summary>
        /// Process disconnect
        /// </summary>
        private async Task ProcessDisconnect(VpnPacket packet)
        {
            string reason = packet.Payload.Length > 0 ?
                Encoding.UTF8.GetString(packet.Payload) : "Client requested disconnect";

            OnLogMessage($"Client disconnect: {reason}", "INFO");

            // Send acknowledgment
            var ackPacket = PacketBuilder.CreateDataPacket(packet.SessionId,
                Encoding.UTF8.GetBytes("DISCONNECT_ACK"));
            await SendPacket(ackPacket);

            Stop(reason);
        }

        /// <summary>
        /// Send packet to client
        /// </summary>
        private async Task SendPacket(VpnPacket packet)
        {
            try
            {
                byte[] packetData = PacketSerializer.Serialize(packet);
                _bytesSent += packetData.Length;

                await _networkStream.WriteAsync(packetData, 0, packetData.Length, _cancellationTokenSource.Token);
                await _networkStream.FlushAsync(_cancellationTokenSource.Token);

                // Update last activity for keep-alive
                _lastActivity = DateTime.Now;
            }
            catch (Exception ex)
            {
                OnLogMessage($"Error sending packet: {ex.Message}", "ERROR");
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
                int sessionId = _session != null ? _session.SessionIdHash : 0;
                var errorPacket = PacketBuilder.CreateErrorPacket(
                    sessionId,
                    errorCode,
                    message);

                await SendPacket(errorPacket);
                OnLogMessage($"Error sent to client: {message} (Code: {errorCode})", "ERROR");
            }
            catch (Exception ex)
            {
                OnLogMessage($"Error sending error packet: {ex.Message}", "ERROR");
            }
        }

        // ====================== EVENT TRIGGERS ======================

        /// <summary>
        /// Trigger client identified event
        /// </summary>
        protected virtual void OnClientIdentified()
        {
            ClientIdentified?.Invoke(this, new ClientIdentifiedEventArgs
            {
                ClientId = _clientId,
                IpAddress = _clientIp,
                Username = _username,
                SessionId = _session?.SessionId ?? string.Empty,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Trigger client disconnected event
        /// </summary>
        protected virtual void OnClientDisconnected(string reason)
        {
            ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs
            {
                ClientId = _clientId,
                IpAddress = _clientIp,
                SessionId = _session?.SessionId ?? string.Empty,
                Reason = reason,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Trigger log message event
        /// </summary>
        protected virtual void OnLogMessage(string message, string level = "INFO")
        {
            LogMessage?.Invoke(this, new ClientLogMessageEventArgs
            {
                Message = message,
                Level = level,
                ClientId = _clientId,
                IpAddress = _clientIp,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Trigger data transfer event
        /// </summary>
        protected virtual void OnDataTransferred(long bytesReceived, long bytesSent, string direction)
        {
            DataTransferred?.Invoke(this, new ClientDataTransferEventArgs
            {
                ClientId = _clientId,
                SessionId = _session?.SessionId ?? string.Empty,
                BytesReceived = bytesReceived,
                BytesSent = bytesSent,
                Direction = direction,
                Timestamp = DateTime.Now
            });
        }

        // ====================== PUBLIC PROPERTIES ======================

        /// <summary>
        /// Get client endpoint string
        /// </summary>
        public string GetClientEndpoint()
        {
            try
            {
                return _tcpClient.Client.RemoteEndPoint?.ToString() ?? _clientIp;
            }
            catch
            {
                return _clientIp;
            }
        }

        /// <summary>
        /// Check if handler is running
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Get client session
        /// </summary>
        public Session? GetSession() => _session;

        /// <summary>
        /// Get client ID
        /// </summary>
        public string ClientId => _clientId;

        /// <summary>
        /// Get client IP address
        /// </summary>
        public string ClientIp => _clientIp;

        /// <summary>
        /// Get username
        /// </summary>
        public string Username => _username;

        /// <summary>
        /// Check if client is authenticated
        /// </summary>
        public bool IsAuthenticated => _isAuthenticated;

        /// <summary>
        /// Get connection time
        /// </summary>
        public DateTime ConnectedAt => _connectedAt;

        /// <summary>
        /// Get bytes sent
        /// </summary>
        public long BytesSent => _bytesSent;

        /// <summary>
        /// Get bytes received
        /// </summary>
        public long BytesReceived => _bytesReceived;

        /// <summary>
        /// Get total bytes transferred
        /// </summary>
        public long TotalBytesTransferred => _bytesSent + _bytesReceived;

        /// <summary>
        /// Get last activity time
        /// </summary>
        public DateTime LastActivity => _lastActivity;

        public void Dispose()
        {
            Stop("Disposed");
            _cryptoManager?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
}
