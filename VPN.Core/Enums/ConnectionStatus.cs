using System;
using System.Collections.Generic;
using System.Text;

namespace VPN.Core.Enums
{
    /// <summary>
    /// Represents connection states in VPN lifecycle
    /// </summary>
    public enum ConnectionStatus
    {
        Disconnected = 0,      // Not connected
        Connecting = 1,        // Connection in progress
        Connected = 2,         // Successfully connected
        Authenticating = 3,    // Authentication phase
        Error = 4,             // Connection error
        Reconnecting = 5,       // Attempting to reconnect
        Disconnecting = 6
    }
}
