using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VPN.Server.Dashboard
{
    public partial class MainWindow : Window
    {
        // Client data model class (nested for simplicity)
        public class ClientInfo
        {
            public string ClientId { get; set; } = string.Empty;
            public string IpAddress { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string StatusColor { get; set; } = string.Empty;
            public string ConnectedTime { get; set; } = string.Empty;
            public string Uptime { get; set; } = string.Empty;
            public string Upload { get; set; } = string.Empty;
            public string Download { get; set; } = string.Empty;
            public long UploadBytes { get; set; }
            public long DownloadBytes { get; set; }
            public DateTime ConnectedAt { get; set; }
        }

        // Observable collections
        private ObservableCollection<ClientInfo> _clients = new ObservableCollection<ClientInfo>();

        // Timers
        private DispatcherTimer? _uiTimer;
        private DispatcherTimer? _uptimeTimer;

        // Server process
        private Process? _serverProcess;
        private bool _isServerRunning = false;
        private DateTime _serverStartTime;

        // Statistics
        private int _totalConnections = 0;
        private long _totalBytesForwarded = 0;
        private int _totalPacketsForwarded = 0;
        private Random _random = new Random();
        private int _logEntryCount = 0;

        public MainWindow()
        {
            InitializeComponent();

            // Setup data binding
            dgClients.ItemsSource = _clients;

            // Setup timers
            InitializeTimers();

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
        }

        // ====================== UI UPDATE METHODS ======================

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

                // Update statistics
                txtActiveClients.Text = _clients.Count.ToString();
                txtTotalConnections.Text = _totalConnections.ToString();
                txtBytesForwarded.Text = FormatBytes(_totalBytesForwarded);
                txtPacketsForwarded.Text = _totalPacketsForwarded.ToString("N0");

                // Simulate CPU and memory usage
                if (_isServerRunning)
                {
                    txtCpuUsage.Text = $"{_random.Next(1, 30)}%";
                    txtMemoryUsage.Text = $"{_random.Next(50, 200)} MB";
                }

                // Update log entry count
                txtLogEntries.Text = $"{_logEntryCount} entries";

                // Auto-scroll log
                if (chkAutoScroll.IsChecked == true)
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

        private void UpdateUptime(object? sender, EventArgs e)
        {
            if (_isServerRunning)
            {
                TimeSpan uptime = DateTime.Now - _serverStartTime;
                txtUptime.Text = uptime.ToString(@"hh\:mm\:ss");
            }
        }

        private void UpdateServerStatusUI()
        {
            if (_isServerRunning)
            {
                indServerStatus.Fill = new SolidColorBrush(Colors.Green);
                txtServerStatus.Text = "Server Running";
                txtServerInfo.Text = $"Port: {txtPort.Text} | Clients: {_clients.Count}";

                btnStartServer.IsEnabled = false;
                btnStopServer.IsEnabled = true;
            }
            else
            {
                indServerStatus.Fill = new SolidColorBrush(Colors.Red);
                txtServerStatus.Text = "Server Stopped";
                txtServerInfo.Text = "Click Start Server to begin";

                btnStartServer.IsEnabled = true;
                btnStopServer.IsEnabled = false;

                txtUptime.Text = "00:00:00";
            }
        }

        // ====================== BUTTON HANDLERS ======================

        private async void btnStartServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("Starting VPN Server...");

                // Update UI
                _isServerRunning = true;
                _serverStartTime = DateTime.Now;
                UpdateServerStatusUI();
                _uptimeTimer?.Start(); // Use null-conditional operator

                // Clear previous clients
                _clients.Clear();

                // Simulate server startup process
                await Task.Delay(500);
                AddLog($"Server starting on port {txtPort.Text}...");
                await Task.Delay(500);
                AddLog("Initializing encryption modules...");
                await Task.Delay(500);
                AddLog("Starting TCP listener...");
                await Task.Delay(300);
                AddLog("✓ VPN Server started successfully");
                AddLog($"Listening on 0.0.0.0:{txtPort.Text}");

                // Start simulated client connections
                StartSimulatedClients();
            }
            catch (Exception ex)
            {
                AddLog($"Error starting server: {ex.Message}");
                _isServerRunning = false;
                UpdateServerStatusUI();
            }
        }

        private async void btnStopServer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("Stopping VPN Server...");

                // Stop timers
                _uptimeTimer?.Stop(); // Use null-conditional operator

                // Update UI
                _isServerRunning = false;
                UpdateServerStatusUI();

                // Simulate server shutdown
                await Task.Delay(300);
                AddLog("Disconnecting all clients...");
                await Task.Delay(300);
                AddLog("Stopping network services...");
                await Task.Delay(300);
                AddLog("✓ VPN Server stopped successfully");

                // Clear clients
                _clients.Clear();
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
            AddLog("Statistics refreshed");
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

        // ====================== SIMULATION METHODS ======================

        private async void StartSimulatedClients()
        {
            // Start a background thread to simulate client connections
            await Task.Run(async () =>
            {
                while (_isServerRunning)
                {
                    await Task.Delay(_random.Next(2000, 10000)); // Random delay between 2-10 seconds

                    if (!_isServerRunning) break;

                    // Simulate new client connection
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        SimulateNewClient();
                    });
                }
            });
        }

        private void SimulateNewClient()
        {
            // Only add clients if we have capacity
            if (_clients.Count >= int.Parse(txtMaxClients.Text))
                return;

            try
            {
                _totalConnections++;

                var client = new ClientInfo
                {
                    ClientId = $"client-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                    IpAddress = $"192.168.1.{_random.Next(2, 254)}",
                    Status = "Connected",
                    StatusColor = "#4CAF50", // Green
                    ConnectedTime = DateTime.Now.ToString("HH:mm:ss"),
                    ConnectedAt = DateTime.Now,
                    UploadBytes = 0,
                    DownloadBytes = 0,
                    Upload = "0 B",
                    Download = "0 B"
                };

                _clients.Add(client);

                // Update statistics
                _totalBytesForwarded += _random.Next(1024, 10240); // 1KB to 10KB
                _totalPacketsForwarded += _random.Next(1, 10);

                AddLog($"New client connected: {client.ClientId} from {client.IpAddress}");

                // Start traffic simulation for this client
                StartClientTrafficSimulation(client);

                // Simulate occasional client disconnections
                if (_random.Next(0, 100) < 20) // 20% chance
                {
                    Task.Delay(_random.Next(10000, 60000)).ContinueWith(_ =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            SimulateClientDisconnection(client);
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                AddLog($"Error simulating client: {ex.Message}");
            }
        }

        private async void StartClientTrafficSimulation(ClientInfo client)
        {
            // Simulate traffic for this client
            while (_isServerRunning && _clients.Contains(client))
            {
                await Task.Delay(_random.Next(1000, 5000));

                if (!_isServerRunning || !_clients.Contains(client))
                    break;

                // Update traffic stats
                client.UploadBytes += _random.Next(512, 5120);
                client.DownloadBytes += _random.Next(512, 5120);

                client.Upload = FormatBytes(client.UploadBytes);
                client.Download = FormatBytes(client.DownloadBytes);
                client.Uptime = FormatTimeSpan(DateTime.Now - client.ConnectedAt);

                // Update global stats
                _totalBytesForwarded += _random.Next(1024, 10240);
                _totalPacketsForwarded += _random.Next(1, 5);

                // Refresh the DataGrid
                var index = _clients.IndexOf(client);
                _clients.RemoveAt(index);
                _clients.Insert(index, client);
            }
        }

        private void SimulateClientDisconnection(ClientInfo client)
        {
            if (_clients.Contains(client))
            {
                client.Status = "Disconnected";
                client.StatusColor = "#F44336"; // Red

                AddLog($"Client disconnected: {client.ClientId}");

                // Remove after 5 seconds
                Task.Delay(5000).ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _clients.Remove(client);
                    });
                });
            }
        }

        // ====================== HELPER METHODS ======================

        private void AddLog(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string logLevel = GetLogLevelPrefix();

                // Filter by log level
                string? selectedLevel = (cmbLogLevel.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
                if (selectedLevel != "All")
                {
                    if (selectedLevel == "Info" && message.Contains("[ERROR]"))
                        return;
                    if (selectedLevel == "Warning" && !message.Contains("[WARN]") && !message.Contains("[ERROR]"))
                        return;
                    if (selectedLevel == "Error" && !message.Contains("[ERROR]"))
                        return;
                }

                txtLog.AppendText($"{timestamp} {logLevel}{message}\n");
                _logEntryCount++;
            }
            catch (Exception ex)
            {
                // Silent fail for log errors
                Debug.WriteLine($"Log error: {ex.Message}");
            }
        }

        private string GetLogLevelPrefix()
        {
            return ""; // No prefix for normal logs
        }

        private string FormatBytes(long bytes)
        {
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

                // Stop server if running
                if (_isServerRunning)
                {
                    var result = MessageBox.Show(
                        "Server is still running. Stop server before closing?",
                        "Confirm Exit",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Simulate server stop
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
                if (!string.IsNullOrWhiteSpace(line))
                {
                    string message = line.Substring(line.IndexOf(']') + 2);
                    AddLog(message);
                }
            }
        }
    }
}