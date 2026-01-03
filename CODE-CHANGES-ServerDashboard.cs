// ==============================================================================
// CODE CHANGES FOR: VPN.Server.Dashboard\MainWindow.xaml.cs
// ==============================================================================

// ============== SECTION 1: ADD USING STATEMENTS (Top of file, after existing usings) ==============
using VPN.Server;
using VPN.Core.Enums;


// ============== SECTION 2: ADD PRIVATE FIELDS (In MainWindow class, after existing fields) ==============

        // VPN Server instance
        private VpnServer? _vpnServer;
        private ServerConfiguration? _serverConfig;
        private DispatcherTimer? _monitoringTimer;


// ============== SECTION 3: ADD LoadServerConfiguration METHOD ==============

        private void LoadServerConfiguration()
        {
            try
            {
                _serverConfig = ServerConfiguration.LoadFromFile();
                
                // Update UI with loaded config
                if (txtPort != null) 
                    txtPort.Text = _serverConfig.Port.ToString();
                
                if (txtMaxClients != null)
                    txtMaxClients.Text = _serverConfig.MaxClients.ToString();
                
                if (chkEncryption != null)
                    chkEncryption.IsChecked = _serverConfig.EnableEncryption;
                
                if (chkAuthentication != null)
                    chkAuthentication.IsChecked = _serverConfig.RequireAuthentication;

                AddLog("Configuration loaded from file");
            }
            catch (Exception ex)
            {
                AddLog($"Could not load configuration: {ex.Message}");
                _serverConfig = new ServerConfiguration();
            }
        }


// ============== SECTION 4: UPDATE CONSTRUCTOR ==============
// Update the existing constructor to call LoadServerConfiguration:

        public MainWindow()
        {
            InitializeComponent();

            // Setup data binding
            dgClients.ItemsSource = _clients;

            // Setup timers
            InitializeTimers();

            // Load configuration  ? ADD THIS LINE
            LoadServerConfiguration();

            // Initial UI setup
            UpdateServerStatusUI();

            // Add initial log entry
            AddLog("VPN Server Dashboard initialized");
            AddLog("Ready to start server");
        }


// ============== SECTION 5: REPLACE btnStartServer_Click METHOD ==============
// Replace the ENTIRE existing method:

        private async void btnStartServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate port
                if (!int.TryParse(txtPort?.Text, out int port) || port < 1 || port > 65535)
                {
                    MessageBox.Show("Invalid port number. Must be between 1 and 65535.",
                        "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPort.Text = "5000";
                    return;
                }

                // Validate max clients
                if (!int.TryParse(txtMaxClients?.Text, out int maxClients) || maxClients < 1)
                {
                    MessageBox.Show("Invalid max clients. Must be at least 1.",
                        "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtMaxClients.Text = "100";
                    return;
                }

                AddLog("Starting VPN Server...");

                // CREATE SERVER CONFIGURATION
                _serverConfig = new ServerConfiguration
                {
                    BindAddress = "0.0.0.0",
                    Port = port,
                    MaxClients = maxClients,
                    RequireAuthentication = chkAuthentication?.IsChecked == true,
                    EnableEncryption = chkEncryption?.IsChecked == true,
                    AdminPassword = "admin123" // Use txtAdminPassword if you have that field
                };

                // Save configuration
                _serverConfig.SaveToFile();

                // CREATE AND START ACTUAL VPN SERVER
                _vpnServer = new VpnServer(_serverConfig);
                
                await Task.Run(() => _vpnServer.Start());

                _isServerRunning = true;
                _serverStartTime = DateTime.Now;
                UpdateServerStatusUI();
                _uptimeTimer?.Start();

                AddLog($"? VPN Server started successfully on port {port}");
                AddLog($"Server listening on 0.0.0.0:{port}");
                AddLog("Ready to accept client connections");
                
                // Clear previous clients
                _clients.Clear();
                
                // Start monitoring real server stats
                StartRealServerMonitoring();
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] Failed to start server: {ex.Message}");
                AddLog($"[ERROR] {ex.StackTrace}");
                MessageBox.Show($"Failed to start server: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _isServerRunning = false;
                UpdateServerStatusUI();
            }
        }


// ============== SECTION 6: REPLACE btnStopServer_Click METHOD ==============
// Replace the ENTIRE existing method:

        private async void btnStopServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("Stopping VPN Server...");

                // Stop monitoring timer
                _monitoringTimer?.Stop();
                _uptimeTimer?.Stop();

                if (_vpnServer != null)
                {
                    // ACTUALLY STOP THE SERVER
                    await Task.Run(() => _vpnServer.Stop());
                    
                    _vpnServer.Dispose();
                    _vpnServer = null;
                    
                    AddLog("? Server stopped successfully");
                    AddLog("All client connections terminated");
                }

                _isServerRunning = false;
                UpdateServerStatusUI();

                // Clear clients
                _clients.Clear();
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] Error stopping server: {ex.Message}");
                MessageBox.Show($"Error stopping server: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


// ============== SECTION 7: REPLACE btnSaveConfig_Click METHOD ==============
// Replace the ENTIRE existing method:

        private void btnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate port
                if (!int.TryParse(txtPort?.Text, out int port) || port < 1 || port > 65535)
                {
                    MessageBox.Show("Invalid port number. Must be between 1 and 65535.",
                        "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    if (txtPort != null) txtPort.Text = "5000";
                    return;
                }

                // Validate max clients
                if (!int.TryParse(txtMaxClients?.Text, out int maxClients) || maxClients < 1)
                {
                    MessageBox.Show("Invalid max clients. Must be at least 1.",
                        "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    if (txtMaxClients != null) txtMaxClients.Text = "100";
                    return;
                }

                // CREATE AND SAVE REAL CONFIGURATION
                var config = new ServerConfiguration
                {
                    BindAddress = "0.0.0.0",
                    Port = port,
                    MaxClients = maxClients,
                    RequireAuthentication = chkAuthentication?.IsChecked == true,
                    EnableEncryption = chkEncryption?.IsChecked == true,
                    AdminPassword = "admin123"
                };

                // Save to file
                config.SaveToFile();

                AddLog("? Configuration saved");
                AddLog($"  Port: {port}");
                AddLog($"  Max Clients: {maxClients}");
                AddLog($"  Encryption: {(config.EnableEncryption ? "Enabled" : "Disabled")}");
                AddLog($"  Authentication: {(config.RequireAuthentication ? "Required" : "Disabled")}");

                MessageBox.Show("Configuration saved successfully. Restart server for changes to take effect.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] Error saving configuration: {ex.Message}");
                MessageBox.Show($"Error saving configuration: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


// ============== SECTION 8: ADD MONITORING METHODS ==============
// Add these NEW methods anywhere in the class:

        private void StartRealServerMonitoring()
        {
            // Stop existing timer if any
            _monitoringTimer?.Stop();

            // Create monitoring timer
            _monitoringTimer = new DispatcherTimer();
            _monitoringTimer.Interval = TimeSpan.FromSeconds(2);
            _monitoringTimer.Tick += (s, e) => UpdateRealServerStatistics();
            _monitoringTimer.Start();

            AddLog("Real-time server monitoring started");
        }

        private void UpdateRealServerStatistics()
        {
            if (_vpnServer == null || !_vpnServer.IsRunning)
                return;

            try
            {
                // Get real connected clients
                var connectedClients = _vpnServer.GetConnectedClients();
                
                // Get session statistics
                var sessionManager = _vpnServer.GetSessionManager();
                var sessionStats = sessionManager.GetSessionStatistics();
                
                // Get packet forwarder statistics
                var packetForwarder = _vpnServer.GetPacketForwarder();
                var forwardStats = packetForwarder.GetStatistics();

                // Update UI with REAL data
                Dispatcher.Invoke(() =>
                {
                    // Update client list
                    UpdateClientList(connectedClients, sessionManager);

                    // Update statistics
                    if (txtActiveClients != null)
                        txtActiveClients.Text = connectedClients.Count.ToString();
                    
                    if (txtTotalConnections != null)
                        txtTotalConnections.Text = sessionStats.total.ToString();
                    
                    if (txtBytesForwarded != null)
                        txtBytesForwarded.Text = FormatBytes(forwardStats.totalBytes);
                    
                    if (txtPacketsForwarded != null)
                        txtPacketsForwarded.Text = forwardStats.totalPackets.ToString("N0");

                    // Update client count
                    if (txtClientCount != null)
                        txtClientCount.Text = connectedClients.Count.ToString();

                    // Update total bytes/packets for tracking
                    _totalBytesForwarded = forwardStats.totalBytes;
                    _totalPacketsForwarded = forwardStats.totalPackets;
                });
            }
            catch (Exception ex)
            {
                AddLog($"[WARN] Monitoring error: {ex.Message}");
            }
        }

        private void UpdateClientList(List<ClientHandler> clients, SessionManager sessionManager)
        {
            // Store current client IDs to detect new connections
            var existingClientIds = _clients.Select(c => c.ClientId).ToHashSet();

            // Clear and rebuild client list
            _clients.Clear();

            // Add real clients
            foreach (var handler in clients)
            {
                if (!handler.IsRunning)
                    continue;

                var session = handler.GetSession();
                if (session != null)
                {
                    _clients.Add(new ClientInfo
                    {
                        ClientId = session.ClientId,
                        IpAddress = session.RemoteEndpoint?.ToString() ?? "Unknown",
                        Status = session.Status.ToString(),
                        StatusColor = GetStatusColor(session.Status.ToString()),
                        ConnectedTime = session.ConnectedAt.ToString("HH:mm:ss"),
                        ConnectedAt = session.ConnectedAt,
                        UploadBytes = session.BytesSent,
                        DownloadBytes = session.BytesReceived,
                        Upload = FormatBytes(session.BytesSent),
                        Download = FormatBytes(session.BytesReceived),
                        Uptime = FormatTimeSpan(DateTime.Now - session.ConnectedAt)
                    });

                    // Log new connection
                    if (!existingClientIds.Contains(session.ClientId))
                    {
                        AddLog($"? New client connected: {session.ClientId} from {session.RemoteEndpoint}");
                        _totalConnections++;
                    }
                }
            }
        }


// ============== SECTION 9: UPDATE Window_Closing METHOD ==============
// Replace the ENTIRE existing method:

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Stop timers
                _uiTimer?.Stop();
                _uptimeTimer?.Stop();
                _monitoringTimer?.Stop();

                // Stop server if running
                if (_isServerRunning && _vpnServer != null)
                {
                    var result = MessageBox.Show(
                        "Server is still running. Stop server before closing?",
                        "Confirm Exit",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        _vpnServer.Stop();
                        _vpnServer.Dispose();
                        _isServerRunning = false;
                        AddLog("Server stopped by user");
                        _clients.Clear();
                    }
                    else if (result == MessageBoxResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }
                }

                AddLog("Dashboard closed");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during shutdown: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


// ============== SECTION 10: REMOVE/COMMENT OUT UNUSED METHODS ==============
// These methods are no longer needed and can be removed or commented out:
// - StartRealClientTracking() (old version)
// - ConnectToRealServer()
// - UpdateUIWithRealData(ServerStatus? status)

// Or just leave them - they won't cause issues
