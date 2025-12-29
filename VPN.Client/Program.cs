using System;
using System.Threading.Tasks;

namespace VPN.Client
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Title = "VPN Client";
            Console.WriteLine("======================================");
            Console.WriteLine("      CUSTOM VPN CLIENT v1.0");
            Console.WriteLine("======================================");

            VpnClient vpnClient = null;

            try
            {
                // Parse command line arguments
                string serverIp = "127.0.0.1";
                int port = 5000;

                if (args.Length >= 1)
                {
                    serverIp = args[0];
                }
                if (args.Length >= 2)
                {
                    if (int.TryParse(args[1], out int parsedPort))
                    {
                        port = parsedPort;
                    }
                }

                // Load or create configuration
                var config = ClientConfiguration.LoadFromFile();
                if (!config.Validate())
                {
                    config = ClientConfiguration.CreateDefault(serverIp, port);
                    config.SaveToFile();
                }

                // Create VPN client
                vpnClient = new VpnClient(config);

                // Subscribe to events
                vpnClient.ConnectionStatusChanged += (s, status) =>
                    Console.WriteLine($"Connection Status: {status}");

                vpnClient.TunnelStatusChanged += (s, status) =>
                    Console.WriteLine($"Tunnel Status: {status}");

                vpnClient.LogMessage += (s, msg) =>
                    Console.WriteLine($"[LOG] {msg}");

                // Connect to server
                bool connected = await vpnClient.ConnectAsync();
                if (!connected)
                {
                    Console.WriteLine("Failed to connect to server. Exiting...");
                    return;
                }

                Console.WriteLine("\nClient commands:");
                Console.WriteLine("  'connect'    - Connect to server");
                Console.WriteLine("  'disconnect' - Disconnect from server");
                Console.WriteLine("  'tunnel'     - Start VPN tunnel");
                Console.WriteLine("  'stoptunnel' - Stop VPN tunnel");
                Console.WriteLine("  'info'       - Show client information");
                Console.WriteLine("  'test'       - Send test data");
                Console.WriteLine("  'cls'        - Clear screen");
                Console.WriteLine("  'exit'       - Disconnect and exit");
                Console.WriteLine("======================================\n");

                // Command loop
                string command;
                do
                {
                    Console.Write("client> ");
                    command = Console.ReadLine()?.ToLower().Trim();

                    switch (command)
                    {
                        case "connect":
                            await vpnClient.ConnectAsync();
                            break;

                        case "disconnect":
                            vpnClient.Disconnect();
                            break;

                        case "tunnel":
                            vpnClient.StartTunnel();
                            break;

                        case "stoptunnel":
                            vpnClient.StopTunnel();
                            break;

                        case "info":
                            vpnClient.DisplayInfo();
                            break;

                        case "test":
                            await SendTestData(vpnClient);
                            break;

                        case "cls":
                            Console.Clear();
                            Console.WriteLine("======================================");
                            Console.WriteLine("      CUSTOM VPN CLIENT v1.0");
                            Console.WriteLine("======================================");
                            break;

                        case "exit":
                            Console.WriteLine("Disconnecting and exiting...");
                            break;

                        case "":
                            break;

                        default:
                            Console.WriteLine($"Unknown command: {command}");
                            break;
                    }

                } while (command != "exit");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            finally
            {
                vpnClient?.Dispose();
            }
        }

        /// <summary>
        /// Send test data through VPN
        /// </summary>
        static async Task SendTestData(VpnClient vpnClient)
        {
            if (!vpnClient.IsConnected)
            {
                Console.WriteLine("Not connected to server");
                return;
            }

            try
            {
                string testData = $"Test message from VPN Client at {DateTime.Now}";
                byte[] data = System.Text.Encoding.UTF8.GetBytes(testData);

                vpnClient.SendData(data);
                Console.WriteLine($"Sent test data: {testData}");

                // Simulate receiving response
                await Task.Delay(100);
                Console.WriteLine("Test data sent successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test failed: {ex.Message}");
            }
        }
    }
}