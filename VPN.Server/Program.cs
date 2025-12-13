using VPN.Server;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("VPN Server Starting...");
        var server = new VpnServer();
        server.Start();

        Console.WriteLine("Press Enter to stop server...");
        Console.ReadLine();
        server.Stop();
    }
}