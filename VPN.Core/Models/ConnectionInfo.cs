using System;
using System.Collections.Generic;
using System.Text;

namespace VPN.Core.Models
{
    public class ConnectionInfo
    {
        public string ServerIp { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 5000;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
