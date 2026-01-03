using System;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VPN.Server
{
    // Data models
    public class ServerStatus
    {
        public bool IsRunning { get; set; }
        public int ConnectedClients { get; set; }
        public long BytesForwarded { get; set; }
        public List<ClientInfo> Clients { get; set; } = new();
    }

    public class ClientInfo
    {
        public string ClientId { get; set; }
        public string IpAddress { get; set; }
        public string Status { get; set; }
        public DateTime ConnectedAt { get; set; }
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
    }

    // Server command processor
    public class ServerCommandProcessor
    {
        private readonly VpnServer _server;

        public ServerCommandProcessor(VpnServer server)
        {
            _server = server;
        }

        public bool ProcessCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return true;

            command = command.ToLower().Trim();

            switch (command)
            {
                case "info":
                case "stats":
                    _server.DisplayServerInfo();
                    break;

                case "cls":
                    ClearConsole();
                    break;

                case "exit":
                    Console.WriteLine("Shutting down server...");
                    return false;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    break;
            }

            return true;
        }

        private void ClearConsole()
        {
            Console.Clear();
            Console.WriteLine("======================================");
            Console.WriteLine("      CUSTOM VPN SERVER v1.0");
            Console.WriteLine("======================================");
        }
    }

    // Named pipe server for dashboard communication
    public class NamedPipeServer
    {
        private readonly VpnServer _vpnServer;
        private bool _isRunning;

        public NamedPipeServer(VpnServer vpnServer)
        {
            _vpnServer = vpnServer;
        }

        public void Start()
        {
            _isRunning = true;
            Task.Run(RunPipeServer);
        }

        public void Stop()
        {
            _isRunning = false;
        }

        private async Task RunPipeServer()
        {
            try
            {
                while (_isRunning)
                {
                    using var pipeServer = new NamedPipeServerStream(
                        "VPNServerPipe",
                        PipeDirection.InOut,
                        maxNumberOfServerInstances: 1);

                    await WaitForConnectionAsync(pipeServer);

                    if (!_isRunning) break;

                    await HandleClientConnection(pipeServer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pipe server error: {ex.Message}");
            }
        }

        private async Task WaitForConnectionAsync(NamedPipeServerStream pipeServer)
        {
            await Task.Run(() => pipeServer.WaitForConnection());
        }

        private async Task HandleClientConnection(NamedPipeServerStream pipeServer)
        {
            try
            {
                // Prepare server status
                var status = new ServerStatus
                {
                    IsRunning = _vpnServer.IsRunning,
                    ConnectedClients = _vpnServer.GetConnectedClients().Count,
                    BytesForwarded = _vpnServer.GetPacketForwarder()?.GetStatistics().totalBytes ?? 0
                };

                // Serialize and send
                string json = JsonSerializer.Serialize(status);
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                await pipeServer.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                pipeServer.Disconnect();
            }
        }
    }

    // Main program
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                await RunServerAsync();
            }
            catch (Exception ex)
            {
                HandleFatalError(ex);
            }
        }

        private static async Task RunServerAsync()
        {
            DisplayHeader();

            // Load configuration
            var config = ServerConfiguration.LoadFromFile();
            config.DisplaySummary();

            // Create and start server
            using var server = new VpnServer(config);
            server.Start();

            // Start named pipe server for dashboard
            var pipeServer = new NamedPipeServer(server);
            pipeServer.Start();

            DisplayCommands();

            // Start command processor
            var commandProcessor = new ServerCommandProcessor(server);
            await RunCommandLoopAsync(commandProcessor);

            // Cleanup
            pipeServer.Stop();
            server.Stop();
        }

        private static void DisplayHeader()
        {
            Console.Title = "VPN Server";
            Console.WriteLine("======================================");
            Console.WriteLine("      CUSTOM VPN SERVER v1.0");
            Console.WriteLine("======================================");
        }

        private static void DisplayCommands()
        {
            Console.WriteLine("\nServer commands:");
            Console.WriteLine("  'info'  - Show server information");
            Console.WriteLine("  'stats' - Show statistics");
            Console.WriteLine("  'cls'   - Clear screen");
            Console.WriteLine("  'exit'  - Stop server and exit");
            Console.WriteLine("======================================\n");
        }

        private static async Task RunCommandLoopAsync(ServerCommandProcessor processor)
        {
            bool continueRunning = true;

            while (continueRunning)
            {
                Console.Write("server> ");
                var command = Console.ReadLine();
                continueRunning = processor.ProcessCommand(command);
            }
        }

        private static void HandleFatalError(Exception ex)
        {
            Console.WriteLine($"\nFatal error: {ex.Message}");
            Console.WriteLine("Stack trace:");
            Console.WriteLine(ex.StackTrace);
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}