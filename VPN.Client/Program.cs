using VPN.Client;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("VPN Client Starting...");
        var client = new VpnClient();
        client.Connect();

        Console.WriteLine("Press Enter to disconnect...");
        Console.ReadLine();
        client.Disconnect();
    }
}