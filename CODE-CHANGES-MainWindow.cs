// ==============================================================================
// CODE CHANGES FOR: VPN.Client.UI\MainWindow.xaml.cs
// ==============================================================================

// ============== SECTION 1: ADD USING STATEMENTS (Top of file) ==============
using VPN.Client;
using VPN.Core.Enums;


// ============== SECTION 2: ADD PRIVATE FIELDS (In MainWindow class, ~line 25) ==============
// Add these fields after the ConnectionState enum:

        // VPN Client instance
        private VpnClient? _vpnClient;
        private ClientConfiguration? _clientConfig;


// ============== SECTION 3: ADD LoadClientConfiguration METHOD (After InitializeApplication) ==============

        private void LoadClientConfiguration()
        {
            try
            {
                _clientConfig = ClientConfiguration.LoadFromFile();
                
                // Update UI with config values
                txtServerAddress.Text = _clientConfig.ServerIp;
                txtPort.Text = _clientConfig.ServerPort.ToString();
                txtUsername.Text = _clientConfig.Username;
                txtPassword.Password = _clientConfig.Password;

                AddLog($"Configuration loaded from file");
            }
            catch (Exception ex)
            {
                AddLog($"Could not load configuration: {ex.Message}");
                _clientConfig = new ClientConfiguration();
            }
        }


// ============== SECTION 4: CALL LoadClientConfiguration (In InitializeApplication method) ==============

        private void InitializeApplication()
        {
            // Setup data binding
            txtSessionDate.Text = DateTime.Now.ToString("dd/MM/yyyy");

            // Setup timers
            InitializeTimers();

            // Initial UI setup
            UpdateConnectionStatusUI();

            // Add initial log entry
            AddLog("VPN Client Control Panel initialized");
            AddLog("Enter server details and click Connect");

            // Load configuration  ? ADD THIS LINE
            LoadClientConfiguration();
        }


// ============== SECTION 5: REPLACE btnConnect_Click METHOD ==============
// Replace the entire existing btnConnect_Click method with this:

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(txtServerAddress.Text))
                {
                    MessageBox.Show("Please enter a server address", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(txtPort.Text, out int port) || port < 1 || port > 65535)
                {
                    MessageBox.Show("Invalid port number. Must be between 1 and 65535.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPort.Text = "5000";
                    return;
                }

                // Update state
                _currentState = ConnectionState.Connecting;
                UpdateConnectionStatusUI();
                pbConnection.Value = 0;

                AddLog($"Attempting to connect to {txtServerAddress.Text}:{port}...");
                AddLog($"Username: {txtUsername.Text}");
                
                // Create client configuration
                _clientConfig = new ClientConfiguration
                {
                    ServerIp = txtServerAddress.Text,
                    ServerPort = port,
                    Username = txtUsername.Text,
                    Password = txtPassword.Password,
                    EnableEncryption = true
                };

                // Save configuration
                _clientConfig.SaveToFile();

                // Create VPN client
                _vpnClient = new VpnClient(_clientConfig);

                // Subscribe to events
                _vpnClient.ConnectionStatusChanged += OnVpnConnectionStatusChanged;
                _vpnClient.LogMessage += OnVpnLogMessage;
                _vpnClient.TunnelStatusChanged += OnVpnTunnelStatusChanged;

                AddLog("Starting connection process...");
                pbConnection.Value = 10;

                // Actually connect to the VPN server
                bool connected = await _vpnClient.ConnectAsync();

                if (connected)
                {
                    _currentState = ConnectionState.Connected;
                    UpdateConnectionStatusUI();
                    pbConnection.Value = 100;
                    txtConnectionPhase.Text = "Connected!";

                    AddLog("? Connection established successfully");
                    AddLog($"? Session ID: {_vpnClient.GetConnectionManager().SessionId}");
                    AddLog("? Encryption: AES-256 enabled");
                    AddLog("? Secure tunnel established");
                    AddLog("Ready to transmit data securely");

                    // Update session ID display
                    txtSessionId.Text = _vpnClient.GetConnectionManager().SessionId;

                    // Clear and initialize graph data
                    _uploadGraphData.Clear();
                    _downloadGraphData.Clear();
                    _totalUploadBytes = 0;
                    _totalDownloadBytes = 0;

                    // Start tunnel
                    if (_vpnClient.IsConnected)
                    {
                        _vpnClient.StartTunnel();
                        AddLog("VPN tunnel started");
                    }
                }
                else
                {
                    _currentState = ConnectionState.Error;
                    UpdateConnectionStatusUI();
                    AddLog("[ERROR] Failed to connect to server");
                    MessageBox.Show("Failed to connect to VPN server. Please check server address and port.",
                        "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] Connection error: {ex.Message}");
                AddLog($"[ERROR] Stack trace: {ex.StackTrace}");
                _currentState = ConnectionState.Error;
                UpdateConnectionStatusUI();
                MessageBox.Show($"Connection error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


// ============== SECTION 6: REPLACE btnDisconnect_Click METHOD ==============
// Replace the entire existing btnDisconnect_Click method with this:

        private async void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("Disconnecting from VPN server...");

                // Update state
                _currentState = ConnectionState.Disconnecting;
                UpdateConnectionStatusUI();

                if (_vpnClient != null)
                {
                    // Actually disconnect
                    _vpnClient.Disconnect("User requested disconnect");
                    
                    // Unsubscribe from events
                    _vpnClient.ConnectionStatusChanged -= OnVpnConnectionStatusChanged;
                    _vpnClient.LogMessage -= OnVpnLogMessage;
                    _vpnClient.TunnelStatusChanged -= OnVpnTunnelStatusChanged;
                    
                    _vpnClient.Dispose();
                    _vpnClient = null;
                }

                await Task.Delay(500);

                // Final state
                _currentState = ConnectionState.Disconnected;
                UpdateConnectionStatusUI();

                AddLog("? Disconnected from VPN server");
                AddLog($"Session summary: Uploaded {FormatBytes(_totalUploadBytes)}, Downloaded {FormatBytes(_totalDownloadBytes)}");
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] Disconnection error: {ex.Message}");
            }
        }


// ============== SECTION 7: ADD EVENT HANDLER METHODS ==============
// Add these three new methods anywhere in the class (e.g., after btnDisconnect_Click):

        // VPN Client event handlers
        private void OnVpnConnectionStatusChanged(object? sender, ConnectionStatus status)
        {
            Dispatcher.Invoke(() =>
            {
                switch (status)
                {
                    case ConnectionStatus.Connecting:
                        AddLog("Status: Connecting...");
                        pbConnection.Value = 30;
                        break;
                    case ConnectionStatus.Connected:
                        AddLog("Status: Connected");
                        pbConnection.Value = 80;
                        break;
                    case ConnectionStatus.Disconnected:
                        AddLog("Status: Disconnected");
                        _currentState = ConnectionState.Disconnected;
                        UpdateConnectionStatusUI();
                        break;
                    case ConnectionStatus.Error:
                        AddLog("Status: Error");
                        _currentState = ConnectionState.Error;
                        UpdateConnectionStatusUI();
                        break;
                }
            });
        }

        private void OnVpnLogMessage(object? sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                AddLog($"[VPN] {message}");
            });
        }

        private void OnVpnTunnelStatusChanged(object? sender, TunnelStatus status)
        {
            Dispatcher.Invoke(() =>
            {
                AddLog($"Tunnel status: {status}");
            });
        }


// ============== SECTION 8: UPDATE UpdateTrafficUI METHOD ==============
// Replace the existing UpdateTrafficUI method with this:

        private void UpdateTrafficUI()
        {
            // Get real statistics if connected
            if (_vpnClient != null && _vpnClient.IsConnected)
            {
                var tunnelStats = _vpnClient.GetTunnelManager().GetStatistics();
                _totalUploadBytes = tunnelStats.bytesSent;
                _totalDownloadBytes = tunnelStats.bytesReceived;

                // Calculate speeds (approximate)
                double elapsedSeconds = Math.Max(1, (DateTime.Now - _sessionStartTime).TotalSeconds);
                double uploadSpeed = tunnelStats.bytesSent / elapsedSeconds;
                double downloadSpeed = tunnelStats.bytesReceived / elapsedSeconds;

                txtUploadSpeed.Text = FormatSpeed(uploadSpeed);
                txtDownloadSpeed.Text = FormatSpeed(downloadSpeed);

                pbUpload.Value = Math.Min(uploadSpeed / 10000 * 100, 100);
                pbDownload.Value = Math.Min(downloadSpeed / 50000 * 100, 100);
            }
            else
            {
                // Simulate when not connected
                double uploadSpeed = _currentState == ConnectionState.Connected ? _random.Next(100, 10000) : 0;
                double downloadSpeed = _currentState == ConnectionState.Connected ? _random.Next(500, 50000) : 0;

                txtUploadSpeed.Text = FormatSpeed(uploadSpeed);
                txtDownloadSpeed.Text = FormatSpeed(downloadSpeed);

                pbUpload.Value = Math.Min(uploadSpeed / 10000 * 100, 100);
                pbDownload.Value = Math.Min(downloadSpeed / 50000 * 100, 100);
            }

            // Update totals
            txtUploadTotal.Text = FormatBytes(_totalUploadBytes) + " total";
            txtDownloadTotal.Text = FormatBytes(_totalDownloadBytes) + " total";
            txtDataTransferred.Text = FormatBytes(_totalUploadBytes + _totalDownloadBytes);

            // Update session data
            txtSessionUpload.Text = FormatBytes(_totalUploadBytes);
            txtSessionDownload.Text = FormatBytes(_totalDownloadBytes);

            // Update latency (simulated for now)
            int latency = _random.Next(20, 200);
            txtLatency.Text = $"{latency} ms";

            // Update latency status indicator
            if (latency < 50)
            {
                indLatency.Fill = new SolidColorBrush(Colors.Green);
                txtLatencyStatus.Text = "Excellent";
            }
            else if (latency < 100)
            {
                indLatency.Fill = new SolidColorBrush(Colors.Orange);
                txtLatencyStatus.Text = "Good";
            }
            else if (latency < 200)
            {
                indLatency.Fill = new SolidColorBrush(Colors.OrangeRed);
                txtLatencyStatus.Text = "Fair";
            }
            else
            {
                indLatency.Fill = new SolidColorBrush(Colors.Red);
                txtLatencyStatus.Text = "Poor";
            }
        }


// ============== SECTION 9: UPDATE Window_Closing METHOD ==============
// Replace the existing Window_Closing method with this:

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Stop timers
                _uiTimer?.Stop();
                _sessionTimer?.Stop();
                _trafficTimer?.Stop();

                // Disconnect if connected
                if (_currentState == ConnectionState.Connected && _vpnClient != null)
                {
                    var result = MessageBox.Show(
                        "You are currently connected to VPN. Disconnect before closing?",
                        "Confirm Exit",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        _vpnClient.Disconnect("Application closing");
                        _vpnClient.Dispose();
                        _currentState = ConnectionState.Disconnected;
                        AddLog("Client closed - Connection terminated");
                    }
                    else if (result == MessageBoxResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }
                }

                AddLog("VPN Client Control Panel closed");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during shutdown: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


// ============== SECTION 10: REMOVE/COMMENT OUT SimulateConnectionProcess ==============
// You can delete or comment out the entire SimulateConnectionProcess method as it's no longer used
