using System;
using System.Threading;

namespace VPN.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "VPN Server";
            Console.WriteLine("======================================");
            Console.WriteLine("      CUSTOM VPN SERVER v1.0");
            Console.WriteLine("======================================");

            try
            {
                // Load configuration
                var config = ServerConfiguration.LoadFromFile();
                config.DisplaySummary();

                // Create and start server
                using var server = new VpnServer(config);
                server.Start();

                Console.WriteLine("\nServer commands:");
                Console.WriteLine("  'info'  - Show server information");
                Console.WriteLine("  'stats' - Show statistics");
                Console.WriteLine("  'cls'   - Clear screen");
                Console.WriteLine("  'exit'  - Stop server and exit");
                Console.WriteLine("======================================\n");

                // Command loop
                string command;
                do
                {
                    Console.Write("server> ");
                    command = Console.ReadLine()?.ToLower().Trim();

                    switch (command)
                    {
                        case "info":
                        case "stats":
                            server.DisplayServerInfo();
                            break;

                        case "cls":
                            Console.Clear();
                            Console.WriteLine("======================================"); 
                            Console.WriteLine("      CUSTOM VPN SERVER v1.0");
                            Console.WriteLine("======================================");
                            break;

                        case "exit":
                            Console.WriteLine("Shutting down server...");
                            break;

                        case "":
                            break;

                        default:
                            Console.WriteLine($"Unknown command: {command}");
                            break;
                    }

                } while (command != "exit");

                // Stop server
                server.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }
    }
}