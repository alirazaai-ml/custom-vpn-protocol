# VPN Project - Complete Error Fixes

## Build Errors Found

```
Error 1: VPN.Server\VpnServer.cs (Lines 338, 360, 368)
CS0426: Event handler type names conflict

Error 2: VPN.Server\ClientHandler.cs (Line 253)
CS1503: Cannot convert 'string' to 'int' in CreateKeepAlivePacket

Error 3: VPN.Server.Dashboard\MainWindow.xaml.cs (Lines 188, 198)
CS0103: 'btnSaveConfig' does not exist
```

## Manual Fix Instructions

### Fix 1: ClientHandler.cs - Session ID Type Fix

**Location:** VPN.Server\ClientHandler.cs

**Line 84** - Add new field after `private DateTime _connectedAt;`:
```csharp
private int _sessionIdHash = 0;
```

**Line 253** - Replace:
```csharp
var keepAlivePacket = PacketBuilder.CreateKeepAlivePacket(_session.SessionId);
```
With:
```csharp
var keepAlivePacket = PacketBuilder.CreateKeepAlivePacket(_sessionIdHash);
```

**Line 365** (in ProcessHandshakeRequest) - Add after `_session = _sessionManager.CreateSession(_clientId, endpoint);`:
```csharp
_sessionIdHash = _session.SessionId.GetHashCode();
```

### Fix 2: VpnServer.cs - Event Handler Naming

**Location:** VPN.Server\VpnServer.cs

**Line 284** - Change:
```csharp
handler.ClientIdentified += OnClientIdentified;
```
To:
```csharp
handler.ClientIdentified += OnClientIdentifiedHandler;
```

**Line 285** - Change:
```csharp
handler.ClientDisconnected += OnClientDisconnectedByHandler;
```
To:
```csharp
handler.ClientDisconnected += OnClientDisconnectedHandler;
```

**Line 338** - Rename method:
```csharp
private void OnClientIdentifiedHandler(object? sender, ClientIdentifiedEventArgs e)
```

**Line 360** - Rename method:
```csharp
private void OnClientDisconnectedHandler(object? sender, ClientDisconnectedEventArgs e)
```

### Fix 3: Dashboard - Null Safety

**Location:** VPN.Server.Dashboard\MainWindow.xaml.cs

**Line 188** - Wrap with null check:
```csharp
if (btnSaveConfig != null)
    btnSaveConfig.IsEnabled = false;
```

**Line 198** - Wrap with null check:
```csharp
if (btnSaveConfig != null)
    btnSaveConfig.IsEnabled = true;
```

## Apply All Fixes

Copy this entire replacement for ClientHandler.cs keep-alive section:

```csharp
/// <summary>
/// Keep-alive monitoring loop
/// </summary>
private async void KeepAliveLoop()
{
    while (_isRunning)
    {
        try
        {
            if ((DateTime.Now - _lastActivity).TotalMilliseconds > _config.SessionTimeout)
            {
                OnLogMessage($"Client timeout - no activity for {_config.SessionTimeout}ms", "WARN");
                Stop("Connection timeout");
                break;
            }

            if (_isAuthenticated && _session != null)
            {
                try
                {
                    var keepAlivePacket = PacketBuilder.CreateKeepAlivePacket(_sessionIdHash);
                    await SendPacket(keepAlivePacket);
                }
                catch
                {
                }
            }

            await Task.Delay(_config.KeepAliveInterval);
        }
        catch (Exception ex)
        {
            OnLogMessage($"Keep-alive loop error: {ex.Message}", "ERROR");
            break;
        }
    }
}
```

After making all changes, rebuild:
```
dotnet build
```

Expected: ? **Build succeeded**
