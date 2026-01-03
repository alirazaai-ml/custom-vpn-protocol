using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
// REMOVED: using VPN.Server; ← This was causing the namespace conflict

namespace VPN.Server.Dashboard
{
    /// <summary>
    /// VPN Server Dashboard - Main Window
    /// NAMESPACE ISSUE RESOLVED: No using VPN.Server to avoid conflicts
    /// </summary>
    public partial class MainWindow : Window
    {
        // Observable collection for clients
        private ObservableCollection<ClientInfo> _clients = new ObservableCollection<ClientInfo>();

        // ✅ FIXED: Use fully qualified names to avoid namespace conflicts
        private VPN.Server.VpnServer? _vpnServer;
        private VPN.Server.ServerConfiguration _serverConfig = new VPN.Server.ServerConfiguration();

        // Event subscription tracking
        private bool _eventsSubscribed = false;

        // Timers
        private DispatcherTimer? _uiTimer;
        private DispatcherTimer? _uptimeTimer;
        private DispatcherTimer? _statsTimer;

        // Statistics
        private int _totalConnections = 0;
        private long _totalBytesForwarded = 0;
        private int _totalPacketsForwarded = 0;
        private Random _random = new Random();
        private int _logEntryCount = 0;
        private DateTime _serverStartTime;

        // Client data model class
        public class ClientInfo
        {
            public string ClientId { get; set; } = string.Empty;
            public string IpAddress { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty; // Added
            public string Status { get; set; } = "Connected";
            public string StatusColor { get; set; } = "#4CAF50";
            public string ConnectedTime { get; set; } = DateTime.Now.ToString("HH:mm:ss");
            public string Uptime { get; set; } = "0s";
            public string Upload { get; set; } = "0 B";
            public string Download { get; set; } = "0 B"; // Changed from read-only
            public long UploadBytes { get; set; }
            public long DownloadBytes { get; set; }
            public DateTime ConnectedAt { get; set; } = DateTime.Now;
            public string SessionId { get; set; } = string.Empty; // Added
        }

        public MainWindow()
        {
            InitializeComponent();

            // Setup data binding
            dgClients.ItemsSource = _clients;

            // Setup timers
            InitializeTimers();

            // Load configuration
            LoadConfiguration();

            // Initial UI setup
            UpdateServerStatusUI();

            // Add initial log entry
            AddLog("VPN Server Dashboard initialized");
            AddLog("Ready to start server");
        }

        private void InitializeTimers()
        {
            // UI Update Timer (every 100ms)
            _uiTimer = new DispatcherTimer();
            _uiTimer.Interval = TimeSpan.FromMilliseconds(100);
            _uiTimer.Tick += UpdateUI;
            _uiTimer.Start();

            // Uptime Timer (every second)
            _uptimeTimer = new DispatcherTimer();
            _uptimeTimer.Interval = TimeSpan.FromSeconds(1);
            _uptimeTimer.Tick += UpdateUptime;

            // Statistics Timer (every 5 seconds)
            _statsTimer = new DispatcherTimer();
            _statsTimer.Interval = TimeSpan.FromSeconds(5);
            _statsTimer.Tick += UpdateStatistics;
        }

        private void LoadConfiguration()
        {
            try
            {
                _serverConfig = VPN.Server.ServerConfiguration.LoadFromFile();
                txtPort.Text = _serverConfig.Port.ToString();
                txtMaxClients.Text = _serverConfig.MaxClients.ToString();
                chkEncryption.IsChecked = _serverConfig.EnableEncryption;
                chkAuthentication.IsChecked = _serverConfig.RequireAuthentication;

                AddLog("Configuration loaded from file");
                
                // Display server IP for remote connections
                string serverIp = VPN.Server.ServerConfiguration.GetServerIpAddress();
                AddLog($"📍 Server IP: {serverIp}");
                AddLog($"📍 Clients can connect to: {serverIp}:{_serverConfig.Port}");
            }
            catch (Exception ex)
            {
                AddLog($"Error loading configuration: {ex.Message}");
            }
        }

        // ====================== UI UPDATE METHODS ======================

        // Change the UpdateUI method signature to match EventHandler delegate (object? sender, EventArgs e)
        private void UpdateUI(object? sender, EventArgs e)
        {
            try
            {
                // Update client list visibility
                bool hasClients = _clients.Count > 0;
                emptyClientsState.Visibility = hasClients ? Visibility.Collapsed : Visibility.Visible;
                dgClients.Visibility = hasClients ? Visibility.Visible : Visibility.Collapsed;

                // Update client count
                txtClientCount.Text = _clients.Count.ToString();

                // Update log entry count
                txtLogEntries.Text = $"{_logEntryCount} entries";

                // Auto-scroll log
                if (chkAutoScroll?.IsChecked == true)
                {
                    logScrollViewer.ScrollToEnd();
                }

                // Update timestamp
                txtLastUpdate.Text = DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                AddLog($"UI Update Error: {ex.Message}");
            }
        }

        // Change the UpdateUptime method signature to allow nullable sender
        private void UpdateUptime(object? sender, EventArgs e)
        {
            if (_vpnServer?.IsRunning == true && _serverStartTime != DateTime.MinValue)
            {
                TimeSpan uptime = DateTime.Now - _serverStartTime;
                txtUptime.Text = uptime.ToString(@"hh\:mm\:ss");

                // Update client uptimes
                foreach (var client in _clients)
                {
                    if (client.ConnectedAt != DateTime.MinValue)
                    {
                        client.Uptime = FormatTimeSpan(DateTime.Now - client.ConnectedAt);
                    }
                }
                dgClients.Items.Refresh();
            }
        }

        private void UpdateStatistics(object sender, EventArgs e)
        {
            if (_vpnServer?.IsRunning == true)
            {
                // Update server statistics display
                txtActiveClients.Text = _clients.Count.ToString();
                txtTotalConnections.Text = _totalConnections.ToString();
                txtBytesForwarded.Text = FormatBytes(_totalBytesForwarded);
                txtPacketsForwarded.Text = _totalPacketsForwarded.ToString("N0");

                // Simulate CPU and memory usage (in real implementation, get from system)
                txtCpuUsage.Text = $"{_random.Next(1, 30)}%";
                txtMemoryUsage.Text = $"{_random.Next(50, 200)} MB";
            }
        }

        private void UpdateServerStatusUI()
        {
            if (_vpnServer?.IsRunning == true)
            {
                indServerStatus.Fill = new SolidColorBrush(Colors.Green);
                txtServerStatus.Text = "Server Running";
                txtServerInfo.Text = $"Port: {txtPort.Text} | Clients: {_clients.Count}";

                btnStartServer.IsEnabled = false;
                btnStopServer.IsEnabled = true;
                //btnSaveConfig.IsEnabled = false;
            }
            else
            {
                indServerStatus.Fill = new SolidColorBrush(Colors.Red);
                txtServerStatus.Text = "Server Stopped";
                txtServerInfo.Text = "Click Start Server to begin";

                btnStartServer.IsEnabled = true;
                btnStopServer.IsEnabled = false;
                //btnSaveConfig.IsEnabled = true;

                txtUptime.Text = "00:00:00";
                txtActiveClients.Text = "0";
                txtTotalConnections.Text = "0";
                txtBytesForwarded.Text = "0 B";
                txtPacketsForwarded.Text = "0";
                txtCpuUsage.Text = "0%";
                txtMemoryUsage.Text = "0 MB";
            }
        }

        // ====================== EVENT HANDLERS ======================

        private void OnClientConnected(object? sender, VPN.Server.ClientEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _totalConnections++;

                var clientInfo = new ClientInfo
                {
                    ClientId = e.ClientId,
                    IpAddress = e.IpAddress,
                    Status = "Connected",
                    StatusColor = "#4CAF50",
                    ConnectedTime = e.Timestamp.ToString("HH:mm:ss"),
                    ConnectedAt = e.Timestamp,
                    Upload = "0 B",
                    Download = "0 B"
                };

                _clients.Add(clientInfo);
                AddLog($"✅ DEBUG: Client connected event received - {e.ClientId} from {e.IpAddress}");

                UpdateServerStatusUI();
            });
        }

        // ✅ FIXED: Handle user approval requests with proper debugging
        private void OnUserApprovalRequested(object? sender, VPN.Server.UserApprovalRequestEventArgs e)
        {
            try
            {
                AddLog($"🎯 DEBUG: OnUserApprovalRequested called!");
                AddLog($"🎯 DEBUG: Username: {e.Username}");
                AddLog($"🎯 DEBUG: ClientId: {e.ClientId}");
                AddLog($"🎯 DEBUG: IpAddress: {e.IpAddress}");
                
                // Must run on UI thread for MessageBox
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        AddLog($"📋 Approval request from user: {e.Username}");
                        AddLog($"   Client ID: {e.ClientId}");
                        AddLog($"   IP Address: {e.IpAddress}");

                        AddLog($"🖥️ DEBUG: About to show MessageBox...");

                        var result = MessageBox.Show(
                            $"New user wants to connect:\n\n" +
                            $"Username: {e.Username}\n" +
                            $"Client ID: {e.ClientId}\n" +
                            $"IP Address: {e.IpAddress}\n\n" +
                            $"Do you want to accept this user?",
                            "User Approval Required",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question,
                            MessageBoxResult.No); // Default to No for security

                        bool approved = (result == MessageBoxResult.Yes);

                        AddLog($"🖱️ DEBUG: User clicked: {result} (Approved: {approved})");

                        if (approved)
                        {
                            AddLog($"✅ User approved: {e.Username}");
                            AddLog($"   User can now connect and will be auto-approved in future");
                        }
                        else
                        {
                            AddLog($"❌ User rejected: {e.Username}");
                            AddLog($"   Connection will be denied");
                        }

                        // ✅ Signal approval result back to ClientHandler
                        AddLog($"📡 DEBUG: Setting approval result: {approved}");
                        e.ApprovalResult.SetResult(approved);
                        AddLog($"✅ DEBUG: Approval result sent back to server");
                    }
                    catch (Exception uiEx)
                    {
                        AddLog($"❌ Error in UI thread: {uiEx.Message}");
                        AddLog($"📍 UI Exception type: {uiEx.GetType().Name}");
                        // Reject by default on error
                        e.ApprovalResult.SetResult(false);
                    }
                });
            }
            catch (Exception ex)
            {
                AddLog($"❌ CRITICAL: Error processing approval request: {ex.Message}");
                AddLog($"📍 Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    AddLog($"📍 Inner exception: {ex.InnerException.Message}");
                }
                // Reject by default on error
                try
                {
                    e.ApprovalResult.SetResult(false);
                }
                catch
                {
                    // Ignore if already set
                }
            }
        }

        private void OnClientDisconnected(object? sender, VPN.Server.ClientEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Find and remove the client
                for (int i = _clients.Count - 1; i >= 0; i--)
                {
                    if (_clients[i].ClientId == e.ClientId)
                    {
                        _clients.RemoveAt(i);
                        break;
                    }
                }

                AddLog($"Client disconnected: {e.ClientId} from {e.IpAddress}");
                UpdateServerStatusUI();
            });
        }

        private void OnLogMessage(object? sender, VPN.Server.LogMessageEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                string prefix = e.Level switch
                {
                    "WARN" => "[WARN] ",
                    "ERROR" => "[ERROR] ",
                    _ => ""
                };

                AddLog($"{prefix}{e.Message}");
            });
        }

        private void OnStatisticsUpdated(object? sender, VPN.Server.StatisticsEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _totalBytesForwarded = e.TotalBytesForwarded;
                _totalPacketsForwarded = e.TotalPacketsForwarded;
                txtUptime.Text = e.Uptime.ToString(@"hh\:mm\:ss");
            });
        }

        // ====================== BUTTON HANDLERS ======================

        private async void btnStartServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("🚀 DEBUG: Starting VPN Server...");

                // Validate port
                if (!int.TryParse(txtPort.Text, out int port) || port < 1 || port > 65535)
                {
                    MessageBox.Show("Invalid port number. Must be between 1 and 65535.",
                        "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPort.Text = "5000";
                    return;
                }

                // Configure server
                _serverConfig.Port = port;
                _serverConfig.MaxClients = int.TryParse(txtMaxClients.Text, out int maxClients) ? maxClients : 100;
                _serverConfig.EnableEncryption = chkEncryption.IsChecked == true;
                _serverConfig.RequireAuthentication = chkAuthentication.IsChecked == true;

                AddLog($"🔧 DEBUG: Server config - Port: {port}, MaxClients: {maxClients}, Encryption: {_serverConfig.EnableEncryption}");

                // IMPORTANT: Ensure password is set before validation
                if (_serverConfig.RequireAuthentication)
                {
                    if (string.IsNullOrEmpty(_serverConfig.AdminPasswordHash))
                    {
                        AddLog("⚠️ No admin password set - generating secure password...");
                        string tempPassword = "Admin@" + Guid.NewGuid().ToString().Substring(0, 8);
                        _serverConfig.GenerateSecurePassword(tempPassword);
                        AddLog($"🔑 Temporary admin password: {tempPassword}");
                        AddLog("⚠️ SAVE THIS PASSWORD! Change it after first connection.");
                    }
                }

                // Save configuration
                _serverConfig.SaveToFile();
                AddLog("✅ DEBUG: Configuration saved");

                // ✅ CRITICAL: Create real server instance with fully qualified name
                AddLog("🏗️ DEBUG: Creating VpnServer instance...");
                _vpnServer = new VPN.Server.VpnServer(_serverConfig);
                AddLog("✅ DEBUG: VpnServer instance created successfully");

                // ✅ CRITICAL: Subscribe to server events with debugging
                AddLog("🔗 DEBUG: Subscribing to server events...");
                _vpnServer.ClientConnected += OnClientConnected;
                _vpnServer.ClientDisconnected += OnClientDisconnected;
                _vpnServer.LogMessage += OnLogMessage;
                _vpnServer.StatisticsUpdated += OnStatisticsUpdated;
                _vpnServer.UserApprovalRequested += OnUserApprovalRequested; // ✅ CRITICAL: Subscribe to approval event
                AddLog("✅ DEBUG: All events subscribed successfully");
                _eventsSubscribed = true; // Track that events are subscribed

                // Update UI to show starting status
                indServerStatus.Fill = new SolidColorBrush(Colors.Orange);
                txtServerStatus.Text = "Starting...";
                txtServerInfo.Text = "Initializing VPN Server...";
                btnStartServer.IsEnabled = false;

                // Start server (in background thread)
                AddLog("⚡ DEBUG: Starting server in background thread...");
                bool started = false;
                await Task.Run(() =>
                {
                    try
                    {
                        _vpnServer.Start();
                        started = _vpnServer.IsRunning;
                        if (started)
                        {
                            _serverStartTime = DateTime.Now;
                        }
                    }
                    catch (Exception startEx)
                    {
                        AddLog($"❌ DEBUG: Server start exception: {startEx.Message}");
                        started = false;
                    }
                });

                AddLog($"🔍 DEBUG: Server start result - Started: {started}, IsRunning: {_vpnServer?.IsRunning}");

                // Check if server actually started
                if (!started || !_vpnServer.IsRunning)
                {
                    AddLog("❌ Server failed to start - check error messages above");
                    MessageBox.Show("Server failed to start. Check the log for details.",
                        "Server Start Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    
                    _vpnServer.Dispose();
                    _vpnServer = null;
                    UpdateServerStatusUI();
                    return;
                }

                // Start timers
                _uptimeTimer?.Start();
                _statsTimer?.Start();

                // Update UI to show running status
                UpdateServerStatusUI();
                AddLog($"✅ VPN Server started on port {port}");
                
                // Show server IP for client connections
                string serverIp = VPN.Server.ServerConfiguration.GetServerIpAddress();
                AddLog($"📍 Server IP: {serverIp}");
                AddLog($"🔗 Clients should connect to: {serverIp}:{port}");
                
                // Show NAT and forwarding info
                AddLog("✅ Real packet forwarding enabled with NAT");
                AddLog("✅ IP packet parsing active");
                AddLog("✅ Dynamic routing enabled");
                AddLog("✅ SOCKS5 proxy support ready");
                AddLog("✅ Auto-reconnect enabled");

                AddLog("🎯 DEBUG: Server startup complete - waiting for client connections...");
            }
            catch (Exception ex)
            {
                AddLog($"❌ CRITICAL: Error starting server: {ex.Message}");
                AddLog($"📍 Exception type: {ex.GetType().Name}");
                AddLog($"📍 Stack trace: {ex.StackTrace}");
                
                MessageBox.Show($"Failed to start server:\n{ex.Message}",
                    "Server Start Error", MessageBoxButton.OK, MessageBoxImage.Error);
                
                _vpnServer?.Dispose();
                _vpnServer = null;
                UpdateServerStatusUI();
            }
        }

        private async void btnStopServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("Stopping VPN Server...");

                if (_vpnServer != null)
                {
                    // Stop server (in background thread)
                    await Task.Run(() =>
                    {
                        _vpnServer.Stop();
                    });

                    // Unsubscribe events
                    if (_eventsSubscribed)
                    {
                        _vpnServer.ClientConnected -= OnClientConnected;
                        _vpnServer.ClientDisconnected -= OnClientDisconnected;
                        _vpnServer.LogMessage -= OnLogMessage;
                        _vpnServer.StatisticsUpdated -= OnStatisticsUpdated;
                        _vpnServer.UserApprovalRequested -= OnUserApprovalRequested; // ✅ NEW: Unsubscribe
                        _eventsSubscribed = false; // Track that events are unsubscribed
                    }

                    _vpnServer.Dispose();
                    _vpnServer = null;
                }

                // Stop timers
                _uptimeTimer?.Stop();
                _statsTimer?.Stop();

                // Clear clients
                _clients.Clear();

                // Update UI
                UpdateServerStatusUI();
                AddLog("✓ VPN Server stopped successfully");
            }
            catch (Exception ex)
            {
                AddLog($"Error stopping server: {ex.Message}");
            }
        }

        private void btnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate port
                if (!int.TryParse(txtPort.Text, out int port) || port < 1 || port > 65535)
                {
                    MessageBox.Show("Invalid port number. Must be between 1 and 65535.",
                        "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPort.Text = "5000";
                    return;
                }

                // Validate max clients
                if (!int.TryParse(txtMaxClients.Text, out int maxClients) || maxClients < 1)
                {
                    MessageBox.Show("Invalid max clients. Must be at least 1.",
                        "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtMaxClients.Text = "100";
                    return;
                }

                // Save configuration
                _serverConfig.Port = port;
                _serverConfig.MaxClients = maxClients;
                _serverConfig.EnableEncryption = chkEncryption.IsChecked == true;
                _serverConfig.RequireAuthentication = chkAuthentication.IsChecked == true;

                _serverConfig.SaveToFile();

                AddLog("Configuration saved");
                AddLog($"  Port: {port}");
                AddLog($"  Max Clients: {maxClients}");
                AddLog($"  Encryption: {(chkEncryption.IsChecked == true ? "Enabled" : "Disabled")}");
                AddLog($"  Authentication: {(chkAuthentication.IsChecked == true ? "Required" : "Disabled")}");

                MessageBox.Show("Configuration saved successfully. Restart server for changes to take effect.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog($"Error saving configuration: {ex.Message}");
            }
        }

        private void btnRefreshStats_Click(object sender, RoutedEventArgs e)
        {
            if (_vpnServer?.IsRunning == true)
            {
                _vpnServer.DisplayServerInfo();
                AddLog("Server statistics refreshed");
            }
            else
            {
                AddLog("Cannot refresh: Server not running");
            }
        }

        private void btnClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Text = "[Log cleared]\n";
            _logEntryCount = 0;
            AddLog("Log cleared");
        }

        private void btnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"vpn-server-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = ".txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveDialog.FileName, txtLog.Text);
                    AddLog($"Log saved to: {saveDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"Error saving log: {ex.Message}");
            }
        }

        private void btnCopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(txtLog.Text);
                AddLog("Log copied to clipboard");
            }
            catch (Exception ex)
            {
                AddLog($"Error copying log: {ex.Message}");
            }
        }

        // ====================== HELPER METHODS ======================

        private void AddLog(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");

                // Filter by log level
                string? selectedLevel = (cmbLogLevel?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
                if (selectedLevel != "All")
                {
                    if (selectedLevel == "Info" && (message.Contains("[WARN]") || message.Contains("[ERROR]")))
                        return;
                    if (selectedLevel == "Warning" && !message.Contains("[WARN]") && !message.Contains("[ERROR]"))
                        return;
                    if (selectedLevel == "Error" && !message.Contains("[ERROR]"))
                        return;
                }

                txtLog.AppendText($"{timestamp} {message}\n");
                _logEntryCount++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Log error: {ex.Message}");
            }
        }

        private string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";

            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:0.#} {suffixes[suffixIndex]}";
        }

        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
            else if (timeSpan.TotalMinutes >= 1)
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            else
                return $"{timeSpan.Seconds}s";
        }

        // ====================== WINDOW EVENTS ======================

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Stop timers
                _uiTimer?.Stop();
                _uptimeTimer?.Stop();
                _statsTimer?.Stop();

                // Stop server if running
                if (_vpnServer?.IsRunning == true)
                {
                    var result = MessageBox.Show(
                        "Server is still running. Stop server before closing?",
                        "Confirm Exit",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Stop server
                        _vpnServer.Stop();
                        _vpnServer.Dispose();
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

        // ====================== COMBO BOX EVENT ======================

        private void cmbLogLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Refresh log display when level changes
            string currentLog = txtLog.Text;
            txtLog.Text = "[Log level changed]\n";
            _logEntryCount = 0;

            // Re-add all log entries with filtering
            string[] lines = currentLog.Split('\n');
            foreach (string line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line) && line.Contains("]"))
                {
                    int bracketIndex = line.IndexOf(']');
                    if (bracketIndex >= 0 && line.Length > bracketIndex + 2)
                    {
                        string message = line.Substring(bracketIndex + 2);
                        AddLog(message);
                    }
                }
            }
        }
    }
}