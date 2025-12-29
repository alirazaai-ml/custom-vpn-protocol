using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace VPN.Client.UI
{
    public partial class MainWindow : Window
    {
        // Connection state
        private enum ConnectionState { Disconnected, Connecting, Connected, Error,
            Disconnecting
        }
        private ConnectionState _currentState = ConnectionState.Disconnected;

        // Timers
        private DispatcherTimer? _uiTimer;
        private DispatcherTimer? _sessionTimer;
        private DispatcherTimer? _trafficTimer;

        // Session tracking
        private DateTime _sessionStartTime;
        private DateTime _lastUpdateTime;
        private Random _random = new Random();

        // Statistics
        private long _totalUploadBytes = 0;
        private long _totalDownloadBytes = 0;
        private int _logEntryCount = 0;
        private List<double> _speedHistory = new List<double>();
        private List<double> _latencyHistory = new List<double>();

        // Graph data
        private List<double> _uploadGraphData = new List<double>();
        private List<double> _downloadGraphData = new List<double>();
        private int _graphMaxPoints = 50;

        public MainWindow()
        {
            InitializeComponent();
            InitializeApplication();
        }

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
        }

        private void InitializeTimers()
        {
            // UI Update Timer (every 100ms)
            _uiTimer = new DispatcherTimer();
            _uiTimer.Interval = TimeSpan.FromMilliseconds(100);
            _uiTimer.Tick += UpdateUI;
            _uiTimer.Start();

            // Session Timer (every second)
            _sessionTimer = new DispatcherTimer();
            _sessionTimer.Interval = TimeSpan.FromSeconds(1);
            _sessionTimer.Tick += UpdateSessionTime;

            // Traffic Simulation Timer (every 2 seconds)
            _trafficTimer = new DispatcherTimer();
            _trafficTimer.Interval = TimeSpan.FromSeconds(2);
            _trafficTimer.Tick += SimulateTraffic;
        }

        // ====================== UI UPDATE METHODS ======================

        // Update the UpdateUI method signature to match the EventHandler delegate's nullability expectations.
        private void UpdateUI(object? sender, EventArgs e)
        {
            try
            {
                // Update connection progress
                UpdateConnectionProgress();

                // Update traffic statistics
                UpdateTrafficUI();

                // Update graph
                UpdateGraph();

                // Auto-scroll log
                if (chkAutoScroll.IsChecked == true)
                {
                    logScrollViewer.ScrollToEnd();
                }

                // Update log entry count
                txtLogEntries.Text = $"{_logEntryCount} entries";

                // Update timestamp
                txtLastUpdate.Text = DateTime.Now.ToString("HH:mm:ss");

                // Update last update time
                _lastUpdateTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                AddLog($"UI Update Error: {ex.Message}");
            }
        }

        private void UpdateSessionTime(object? sender, EventArgs e)
        {
            if (_currentState == ConnectionState.Connected)
            {
                TimeSpan sessionTime = DateTime.Now - _sessionStartTime;
                txtSessionTime.Text = sessionTime.ToString(@"hh\:mm\:ss");
            }
        }

        private void SimulateTraffic(object? sender, EventArgs e)
        {
            if (_currentState != ConnectionState.Connected)
                return;

            // Simulate network traffic
            SimulateNetworkActivity();
        }

        private void UpdateConnectionProgress()
        {
            if (_currentState == ConnectionState.Connecting)
            {
                // Animate connection progress
                double currentValue = pbConnection.Value;
                if (currentValue < 100)
                {
                    pbConnection.Value = Math.Min(currentValue + 2, 100);

                    // Update phase text
                    if (currentValue < 25)
                        txtConnectionPhase.Text = "Connecting to server...";
                    else if (currentValue < 50)
                        txtConnectionPhase.Text = "Handshaking...";
                    else if (currentValue < 75)
                        txtConnectionPhase.Text = "Authenticating...";
                    else if (currentValue < 95)
                        txtConnectionPhase.Text = "Establishing tunnel...";
                    else
                        txtConnectionPhase.Text = "Finalizing connection...";
                }
            }
        }

        private void UpdateTrafficUI()
        {
            // Calculate speeds (simulated)
            double uploadSpeed = _random.Next(100, 10000); // 100 B/s to 10 KB/s
            double downloadSpeed = _random.Next(500, 50000); // 500 B/s to 50 KB/s

            // Update speed displays
            txtUploadSpeed.Text = FormatSpeed(uploadSpeed);
            txtDownloadSpeed.Text = FormatSpeed(downloadSpeed);

            // Update progress bars
            pbUpload.Value = Math.Min(uploadSpeed / 10000 * 100, 100);
            pbDownload.Value = Math.Min(downloadSpeed / 50000 * 100, 100);

            // Update totals
            txtUploadTotal.Text = FormatBytes(_totalUploadBytes) + " total";
            txtDownloadTotal.Text = FormatBytes(_totalDownloadBytes) + " total";
            txtDataTransferred.Text = FormatBytes(_totalUploadBytes + _totalDownloadBytes);

            // Update session data
            txtSessionUpload.Text = FormatBytes(_totalUploadBytes);
            txtSessionDownload.Text = FormatBytes(_totalDownloadBytes);

            // Update latency (simulated)
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

        private void UpdateGraph()
        {
            // Clear existing graph
            cnvGraph.Children.Clear();

            if (_uploadGraphData.Count < 2)
                return;

            double canvasWidth = cnvGraph.ActualWidth;
            double canvasHeight = cnvGraph.ActualHeight;

            if (canvasWidth <= 0 || canvasHeight <= 0)
                return;

            // Find max value for scaling
            double maxValue = 0;
            foreach (var value in _uploadGraphData)
                maxValue = Math.Max(maxValue, value);
            foreach (var value in _downloadGraphData)
                maxValue = Math.Max(maxValue, value);

            if (maxValue == 0) maxValue = 1;

            // Draw grid lines
            for (int i = 1; i <= 4; i++)
            {
                double y = canvasHeight * i / 5;
                Line gridLine = new Line
                {
                    X1 = 0,
                    X2 = canvasWidth,
                    Y1 = y,
                    Y2 = y,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection(new double[] { 2, 2 })
                };
                cnvGraph.Children.Add(gridLine);
            }

            // Draw upload line (purple)
            Polyline uploadLine = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(156, 39, 176)), // Purple
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };

            // Draw download line (green)
            Polyline downloadLine = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0, 200, 83)), // Green
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round
            };

            double xStep = canvasWidth / (_graphMaxPoints - 1);

            for (int i = 0; i < _uploadGraphData.Count; i++)
            {
                double x = i * xStep;
                double uploadY = canvasHeight - (_uploadGraphData[i] / maxValue * canvasHeight * 0.8);
                double downloadY = canvasHeight - (_downloadGraphData[i] / maxValue * canvasHeight * 0.8);

                uploadLine.Points.Add(new Point(x, uploadY));
                downloadLine.Points.Add(new Point(x, downloadY));
            }

            cnvGraph.Children.Add(uploadLine);
            cnvGraph.Children.Add(downloadLine);

            // Update graph status
            txtGraphStatus.Text = $"Upload: {FormatSpeed(_uploadGraphData[^1])} | Download: {FormatSpeed(_downloadGraphData[^1])}";
        }

        private void SimulateNetworkActivity()
        {
            // Simulate upload traffic
            long uploadBytes = _random.Next(1024, 10240); // 1KB to 10KB
            _totalUploadBytes += uploadBytes;

            // Simulate download traffic  
            long downloadBytes = _random.Next(5120, 51200); // 5KB to 50KB
            _totalDownloadBytes += downloadBytes;

            // Add to graph data
            _uploadGraphData.Add(uploadBytes / 2.0); // Convert to KB/s
            _downloadGraphData.Add(downloadBytes / 2.0);

            // Keep graph data within limits
            if (_uploadGraphData.Count > _graphMaxPoints)
            {
                _uploadGraphData.RemoveAt(0);
                _downloadGraphData.RemoveAt(0);
            }
        }

        private void UpdateConnectionStatusUI()
        {
            switch (_currentState)
            {
                case ConnectionState.Disconnected:
                    indConnectionStatus.Fill = new SolidColorBrush(Colors.Red);
                    txtConnectionStatus.Text = "DISCONNECTED";
                    txtServerInfo.Text = "Not connected to any server";
                    txtServerDetails.Text = "No server connected";
                    txtEncryption.Text = "None";
                    txtProtocol.Text = "TCP";
                    txtSessionId.Text = "Not connected";

                    btnConnect.IsEnabled = true;
                    btnDisconnect.IsEnabled = false;

                    _sessionTimer?.Stop();
                    _trafficTimer?.Stop();
                    break;

                case ConnectionState.Connecting:
                    indConnectionStatus.Fill = new SolidColorBrush(Colors.Orange);
                    txtConnectionStatus.Text = "CONNECTING";
                    txtServerInfo.Text = $"Connecting to {txtServerAddress.Text}:{txtPort.Text}";
                    txtServerDetails.Text = $"Establishing connection to {txtServerAddress.Text}";
                    txtEncryption.Text = "Negotiating...";
                    txtProtocol.Text = "TCP";
                    txtSessionId.Text = "Establishing...";

                    btnConnect.IsEnabled = false;
                    btnDisconnect.IsEnabled = true;
                    break;

                case ConnectionState.Connected:
                    indConnectionStatus.Fill = new SolidColorBrush(Colors.Green);
                    txtConnectionStatus.Text = "CONNECTED";
                    txtServerInfo.Text = $"Connected to {txtServerAddress.Text}:{txtPort.Text}";
                    txtServerDetails.Text = $"✓ Secure connection established\n✓ Data encrypted (AES-256)\n✓ Tunnel active";
                    txtEncryption.Text = "AES-256";
                    txtProtocol.Text = "TCP/TLS";
                    txtSessionId.Text = $"SESS-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";

                    btnConnect.IsEnabled = false;
                    btnDisconnect.IsEnabled = true;

                    // Start timers
                    _sessionStartTime = DateTime.Now;
                    _sessionTimer?.Start();
                    _trafficTimer?.Start();
                    break;

                case ConnectionState.Error:
                    indConnectionStatus.Fill = new SolidColorBrush(Colors.Red);
                    txtConnectionStatus.Text = "ERROR";
                    txtServerInfo.Text = "Connection failed";
                    txtServerDetails.Text = "Failed to establish connection";

                    btnConnect.IsEnabled = true;
                    btnDisconnect.IsEnabled = false;
                    break;
            }
        }

        // ====================== BUTTON HANDLERS ======================

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
                AddLog($"Password: {new string('*', txtPassword.Password.Length)}");
                AddLog("Starting connection process...");

                // Simulate connection process
                await SimulateConnectionProcess();

                // Check if connection was successful
                if (_currentState == ConnectionState.Connecting)
                {
                    _currentState = ConnectionState.Connected;
                    UpdateConnectionStatusUI();
                    pbConnection.Value = 100;
                    txtConnectionPhase.Text = "Connected!";

                    AddLog("✓ Connection established successfully");
                    AddLog($"✓ Session ID: {txtSessionId.Text}");
                    AddLog("✓ Encryption: AES-256 enabled");
                    AddLog("✓ Secure tunnel established");
                    AddLog("Ready to transmit data securely");

                    // Clear and initialize graph data
                    _uploadGraphData.Clear();
                    _downloadGraphData.Clear();
                    _totalUploadBytes = 0;
                    _totalDownloadBytes = 0;
                }
            }
            catch (Exception ex)
            {
                AddLog($"Connection error: {ex.Message}");
                _currentState = ConnectionState.Error;
                UpdateConnectionStatusUI();
            }
        }

        private async void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddLog("Disconnecting from VPN server...");

                // Update state
                _currentState = ConnectionState.Disconnecting;
                UpdateConnectionStatusUI();

                // Simulate disconnection process
                await Task.Delay(800);
                AddLog("Closing secure tunnel...");
                await Task.Delay(500);
                AddLog("Terminating encryption session...");
                await Task.Delay(300);

                // Final state
                _currentState = ConnectionState.Disconnected;
                UpdateConnectionStatusUI();

                AddLog("✓ Disconnected from VPN server");
                AddLog($"Session summary: Uploaded {FormatBytes(_totalUploadBytes)}, Downloaded {FormatBytes(_totalDownloadBytes)}");
            }
            catch (Exception ex)
            {
                AddLog($"Disconnection error: {ex.Message}");
            }
        }

        private async Task SimulateConnectionProcess()
        {
            // Simulate connection steps with delays
            AddLog("Step 1: Resolving server address...");
            await Task.Delay(800);
            pbConnection.Value = 10;

            AddLog("Step 2: Establishing TCP connection...");
            await Task.Delay(1200);
            pbConnection.Value = 25;

            AddLog("Step 3: Performing handshake...");
            await Task.Delay(1000);
            pbConnection.Value = 40;

            AddLog("Step 4: Authenticating...");
            await Task.Delay(1500);
            pbConnection.Value = 60;

            AddLog("Step 5: Negotiating encryption...");
            await Task.Delay(1300);
            pbConnection.Value = 80;

            AddLog("Step 6: Creating secure tunnel...");
            await Task.Delay(900);
            pbConnection.Value = 95;
        }

        // Quick action buttons
        private void btnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Testing connection to server...");
            Task.Delay(1000).ContinueWith(_ =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AddLog("✓ Server is reachable");
                    AddLog("  Latency: 45ms");
                    AddLog("  Response: OK");
                });
            });
        }

        private void btnPingServer_Click(object sender, RoutedEventArgs e)
        {
            AddLog($"Pinging {txtServerAddress.Text}...");
            Task.Delay(800).ContinueWith(_ =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    int latency = _random.Next(20, 100);
                    AddLog($"Ping response: {latency}ms");
                });
            });
        }

        private void btnCheckEncryption_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState == ConnectionState.Connected)
            {
                AddLog("Checking encryption status...");
                AddLog("✓ AES-256 encryption active");
                AddLog("✓ Key exchange: ECDH-256");
                AddLog("✓ HMAC: SHA-256");
                AddLog("✓ Perfect forward secrecy: Enabled");
            }
            else
            {
                AddLog("Cannot check encryption: Not connected");
            }
        }

        private void btnSpeedTest_Click(object sender, RoutedEventArgs e)
        {
            AddLog("Running speed test...");

            Task.Run(async () =>
            {
                await Task.Delay(1000);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    double uploadSpeed = _random.Next(5000, 20000);
                    double downloadSpeed = _random.Next(20000, 100000);

                    AddLog("Speed test results:");
                    AddLog($"  Upload: {FormatSpeed(uploadSpeed)}");
                    AddLog($"  Download: {FormatSpeed(downloadSpeed)}");
                    AddLog($"  Latency: {_random.Next(15, 80)}ms");
                    AddLog($"  Jitter: {_random.Next(1, 10)}ms");
                });
            });
        }

        private void btnReconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState == ConnectionState.Connected)
            {
                AddLog("Reconnecting to server...");
                btnDisconnect_Click(sender, e);

                Task.Delay(2000).ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        btnConnect_Click(sender, e);
                    });
                });
            }
            else
            {
                AddLog("Cannot reconnect: Not currently connected");
            }
        }

        private void btnResetStats_Click(object sender, RoutedEventArgs e)
        {
            _totalUploadBytes = 0;
            _totalDownloadBytes = 0;
            _uploadGraphData.Clear();
            _downloadGraphData.Clear();

            AddLog("Statistics reset");
            AddLog("All traffic counters cleared");
        }

        private void btnClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Text = "[Log cleared]\n";
            _logEntryCount = 0;
            AddLog("Log cleared");
        }

        private void btnExportLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"vpn-client-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = ".txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveDialog.FileName, txtLog.Text);
                    AddLog($"Log exported to: {saveDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"Error exporting log: {ex.Message}");
            }
        }

        // ====================== HELPER METHODS ======================

        private void AddLog(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");

                // Filter by log level
                string? selectedLevel = (cmbLogLevel.SelectedItem as ComboBoxItem)?.Content.ToString();
                if (selectedLevel != "All Levels")
                {
                    if (selectedLevel == "Info Only" && (message.Contains("[WARN]") || message.Contains("[ERROR]")))
                        return;
                    if (selectedLevel == "Warnings" && !message.Contains("[WARN]"))
                        return;
                    if (selectedLevel == "Errors" && !message.Contains("[ERROR]"))
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
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:0.##} {suffixes[suffixIndex]}";
        }

        private string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond < 1024)
                return $"{bytesPerSecond:0} B/s";
            else if (bytesPerSecond < 1024 * 1024)
                return $"{(bytesPerSecond / 1024):0.#} KB/s";
            else
                return $"{(bytesPerSecond / (1024 * 1024)):0.#} MB/s";
        }

        // ====================== WINDOW EVENTS ======================

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Stop timers
                _uiTimer?.Stop();
                _sessionTimer?.Stop();
                _trafficTimer?.Stop();

                // Disconnect if connected
                if (_currentState == ConnectionState.Connected)
                {
                    var result = MessageBox.Show(
                        "You are currently connected to VPN. Disconnect before closing?",
                        "Confirm Exit",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Simulate disconnection
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

        // TextBox enter key handler
        private void txtServerAddress_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                btnConnect.Focus();
                btnConnect_Click(sender, e);
            }
        }

        private void txtPort_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                btnConnect.Focus();
                btnConnect_Click(sender, e);
            }
        }

        private void txtPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                btnConnect.Focus();
                btnConnect_Click(sender, e);
            }
        }
    }
}