# ?? VPN Solution - Quick Start Guide (After Permission Fix)

## ? The Permission Issue is FIXED!

Configuration files now save to a safe location:
```
C:\Users\[YourUsername]\AppData\Roaming\VPN-Solution\
```

---

## ?? How to Run Your VPN (Step-by-Step)

### Step 1: Open Two Terminals in Visual Studio

**Terminal 1 (Server):**
- View ? Terminal ? New Terminal
- Or press: `Ctrl + Shift + ` (backtick)

**Terminal 2 (Client):**
- Click the `+` button in terminal panel
- Or press: `Ctrl + Shift + ` again

---

### Step 2: Start the Server

**In Terminal 1, type:**
```powershell
cd VPN.Server.Dashboard
dotnet run
```

**Expected Output:**
```
===============================================
?? FIRST TIME SETUP - SECURE ADMIN PASSWORD
===============================================
Admin Password: Xy9#mK2pL5@qR8vN
===============================================
??  IMPORTANT: Save this password immediately!
===============================================
? Configuration saved to: C:\Users\...\AppData\Roaming\VPN-Solution\server-config.json
```

**IMPORTANT:** ?? **Copy this password!** You'll need it in Step 4.

---

### Step 3: Start Server in Dashboard

The **VPN Server Dashboard** window will open.

1. ? Click **"Start Server"** button
2. ? Watch the log for: `"? VPN Server started on port 5000"`

**Dashboard should show:**
```
Server Status: Server Running (green indicator)
Port: 5000
Clients: 0
```

---

### Step 4: Start the Client

**In Terminal 2, type:**
```powershell
cd VPN.Client.UI
dotnet run
```

The **VPN Client Control Panel** window will open.

---

### Step 5: Connect Client to Server

In the **Client UI:**

1. **Server Address:** Enter `127.0.0.1` (already filled)
2. **Port:** Enter `5000` (already filled)
3. **Username:** Enter `admin` (or any name)
4. **Password:** **Paste the password from Step 2** ??
5. Click **"Connect"** button

---

### Step 6: Watch the Magic! ?

**Server Dashboard will show:**
```
23:45:00 New client connected from 127.0.0.1
23:45:00 Handshake request from client-abc123 (admin)
23:45:00 Client authenticated successfully: client-abc123
```

**Client will show:**
```
23:45:00 Attempting to connect to 127.0.0.1:5000...
23:45:00 Username: admin
23:45:01 ? Connection established successfully
23:45:01 ? Session ID: [session-guid]
23:45:01 ? Encryption: AES-256 enabled
23:45:01 ? Secure tunnel established
23:45:01 Ready to transmit data securely
```

**Connection Status:** ?? **CONNECTED** (green indicator)

---

## ?? What You Can Do Now

### In the Client UI:

**Quick Actions:**
- ?? **Test Connection** - Verify server is reachable
- ?? **Ping Server** - Check latency
- ?? **Check Encryption** - Verify AES-256 is active
- ? **Speed Test** - Test upload/download speeds

**Monitor:**
- ?? Real-time traffic graph
- ?? Upload/Download speeds
- ?? Session duration
- ?? Data transferred

### In the Server Dashboard:

**View:**
- ?? Connected clients table
- ?? Server statistics (uptime, bytes, packets)
- ?? CPU and memory usage
- ?? Real-time logs

**Manage:**
- ?? Change server configuration
- ?? Export logs
- ??? Clear logs
- ?? Refresh statistics

---

## ?? Testing Features

### Test 1: Basic Connectivity ?
```
1. Connect client to server
2. Check green status indicator
3. Verify session ID in both apps
```

### Test 2: Encryption ?
```
1. After connecting, click "Check Encryption" in client
2. Should show: "? AES-256 encryption active"
3. Server dashboard shows "Encryption: ? Enabled"
```

### Test 3: Speed Test ?
```
1. Click "Speed Test" button in client
2. Watch traffic graph animate
3. Check server dashboard for bytes forwarded
```

### Test 4: Reconnection ?
```
1. Click "Disconnect" in client
2. Click "Reconnect" 
3. Should automatically reconnect
```

### Test 5: Multiple Clients ?
```
1. Start another client instance
2. Connect with different username
3. Server dashboard shows both clients
```

---

## ?? What the UI Shows

### Client Connection Status States:

| Indicator | Status | Description |
|-----------|--------|-------------|
| ?? Red | Disconnected | Not connected to server |
| ?? Orange | Connecting | Connection in progress |
| ?? Green | Connected | Secure connection active |
| ?? Red | Error | Connection failed |

### Server Status States:

| Indicator | Status | Description |
|-----------|--------|-------------|
| ?? Red | Stopped | Server not running |
| ?? Green | Running | Server active, listening on port |

---

## ?? Security Features Active

When connected, you have:

? **AES-256-CBC Encryption** - Military-grade encryption  
? **SHA-256 HMAC** - Data integrity verification  
? **PBKDF2 Password Hashing** - 600,000 iterations  
? **Session Management** - Unique session IDs  
? **Keep-Alive Monitoring** - Connection health checks  
? **Rate Limiting** - DDoS protection  

---

## ?? UI Features Explained

### Client UI Panels:

**1. Connection Panel** (Top)
- Server address input
- Port number
- Username/Password
- Connect/Disconnect buttons

**2. Status Panel** (Middle-Left)
- Connection status indicator
- Server information
- Session details
- Encryption status

**3. Traffic Panel** (Middle-Right)
- Real-time graph
- Upload/Download speeds
- Session statistics
- Latency monitoring

**4. Quick Actions** (Bottom)
- Test Connection
- Ping Server
- Check Encryption
- Speed Test
- Reconnect
- Reset Stats

**5. Log Panel** (Bottom)
- Connection events
- Data transfer logs
- Error messages
- Filter by log level

### Server Dashboard Panels:

**1. Server Control** (Top)
- Start/Stop server
- Configuration settings
- Port and max clients

**2. Statistics Panel** (Middle-Left)
- Active clients count
- Total connections
- Bytes forwarded
- Packets forwarded
- CPU/Memory usage
- Server uptime

**3. Clients Table** (Middle)
- Client ID
- IP Address
- Connection time
- Upload/Download
- Session duration
- Status

**4. Log Panel** (Bottom)
- Server events
- Client connections/disconnections
- Authentication logs
- Error messages

---

## ??? Configuration

### Server Configuration (Auto-saved):
```
Location: %AppData%\VPN-Solution\server-config.json
```

You can change:
- Port number
- Max clients
- Enable/Disable encryption
- Enable/Disable authentication

### Client Configuration (Auto-saved):
```
Location: %AppData%\VPN-Solution\client-config.json
```

Saves your:
- Last server address
- Last port used
- Client ID
- Preferences

---

## ?? Tips & Tricks

### Pro Tip 1: Keep Server Running
Leave the server dashboard open while testing. The console shows real-time connection details.

### Pro Tip 2: Use Localhost for Testing
Always use `127.0.0.1` for local testing. It's faster and doesn't need firewall configuration.

### Pro Tip 3: Save the Password
The auto-generated password is shown only once! Copy it immediately or you'll need to delete the config file to regenerate.

### Pro Tip 4: Monitor Logs
Both server and client show detailed logs. Use the log filter dropdown to focus on specific message types.

### Pro Tip 5: Export Logs
Before closing, use "Export Log" to save connection details for later review.

---

## ?? Common Issues & Solutions

### Issue: "Connection timeout"
**Solution:** Make sure server is running and "Start Server" button was clicked.

### Issue: "Authentication failed"
**Solution:** Use the exact password from server's first run output.

### Issue: Client shows "Connecting..." forever
**Solution:** 
1. Check server dashboard shows "Server Running" (green)
2. Verify port 5000 is correct
3. Try clicking "Disconnect" then "Connect" again

### Issue: Can't see traffic graph
**Solution:** Traffic graph shows data after connection is established and some data is transferred.

### Issue: Server dashboard empty
**Solution:** Click "Start Server" button first, then connect client.

---

## ?? Quick Commands Reference

### Build Everything:
```powershell
dotnet build
```

### Run Server Dashboard:
```powershell
cd VPN.Server.Dashboard
dotnet run
```

### Run Client UI:
```powershell
cd VPN.Client.UI
dotnet run
```

### Run Server Console (Alternative):
```powershell
cd VPN.Server
dotnet run
```

### Run Client Console (Alternative):
```powershell
cd VPN.Client
dotnet run 127.0.0.1 5000
```

### Clean Build:
```powershell
dotnet clean
dotnet build
```

---

## ?? Success Checklist

- [ ] Server dashboard opens without errors
- [ ] Server starts successfully (green indicator)
- [ ] Client UI opens without errors
- [ ] Client connects successfully (green indicator)
- [ ] Traffic graph shows activity
- [ ] Encryption status shows "AES-256"
- [ ] Server dashboard shows connected client
- [ ] Both logs show connection messages
- [ ] Speed test works
- [ ] Disconnect and reconnect works

**If all checked:** ?? **CONGRATULATIONS! Your VPN is working perfectly!**

---

## ?? Next Steps

1. ? Test multiple client connections
2. ? Try different server configurations
3. ? Monitor traffic statistics
4. ? Test reconnection scenarios
5. ? Export and review logs
6. ? Test with different ports

---

## ?? Need Help?

**Check these files:**
- `PROJECT-READINESS-REPORT.md` - Full feature documentation
- `FIX-FILE-PERMISSION-ERROR.md` - Permission issue details
- Server logs in dashboard
- Client logs in UI

**Common Paths:**
- Config: `%AppData%\VPN-Solution\`
- Solution: `D:\Ali BSCS\third semester\Computer Networking\CN project\VPN-Solution\`

---

**Last Updated:** December 2024  
**Status:** ? Ready to Run  
**Permission Issue:** ? **FIXED**

---

**Enjoy your secure VPN connection! ????**
