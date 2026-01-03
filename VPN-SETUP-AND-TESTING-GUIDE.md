# ?? VPN Project - Complete Setup & Testing Guide

## ?? **PROJECT OVERVIEW**
This guide will walk you through setting up, running, and testing your custom VPN solution to verify that it properly encrypts and decrypts data.

---

## ??? **PREREQUISITES**

### **Software Requirements:**
- ? **Visual Studio 2022** (Community, Professional, or Enterprise)
- ? **.NET 10 SDK** installed
- ? **Windows 10/11** (required for WPF applications)
- ? **Administrator privileges** (for firewall rules)

### **Hardware Requirements:**
- **RAM:** Minimum 8GB (16GB recommended)
- **Storage:** At least 1GB free space
- **Network:** Active internet connection for testing

---

## ??? **STEP 1: PROJECT SETUP**

### **1.1 Clone and Open Project**
```bash
# Your project is already cloned at:
# D:\Ali BSCS\third semester\Computer Networking\CN project\VPN-Solution\

# Open in Visual Studio
# File ? Open ? Project/Solution ? Select VPN-Solution.sln
```

### **1.2 Verify Project Structure**
Your solution should contain these projects:
```
VPN-Solution/
??? VPN.Core/              # Shared core components
??? VPN.Server/            # VPN server backend
??? VPN.Client/            # VPN client backend
??? VPN.Client.UI/         # Client desktop application
??? VPN.Server.Dashboard/  # Server admin dashboard
??? VPN.Tests/             # Unit tests (newly added)
```

### **1.3 Build Solution**
```bash
# In Visual Studio:
# Build ? Rebuild Solution (Ctrl+Shift+B)

# Or via command line:
cd "D:\Ali BSCS\third semester\Computer Networking\CN project\VPN-Solution"
dotnet build
```

**Expected Output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## ?? **STEP 2: CONFIGURE WINDOWS FIREWALL**

### **2.1 Allow VPN Server Port**
Run PowerShell as Administrator:
```powershell
# Allow VPN server port
netsh advfirewall firewall add rule name="VPN Server" dir=in action=allow protocol=TCP localport=5000

# Verify rule was added
netsh advfirewall firewall show rule name="VPN Server"
```

### **2.2 Allow SOCKS Proxy Port**
```powershell
# Allow SOCKS proxy port  
netsh advfirewall firewall add rule name="VPN SOCKS Proxy" dir=in action=allow protocol=TCP localport=1080
```

---

## ??? **STEP 3: START VPN SERVER**

### **3.1 Launch Server Dashboard**
1. In Visual Studio Solution Explorer
2. Right-click **VPN.Server.Dashboard** project
3. Select **"Set as Startup Project"**
4. Press **F5** (Start Debugging) or **Ctrl+F5** (Start Without Debugging)

### **3.2 Start the Server**
1. Server Dashboard window opens
2. Click **"START SERVER"** button
3. Wait for confirmation messages:

**Expected Server Output:**
```
?? Server IP: 192.168.0.108
?? Clients can connect to: 192.168.0.108:5000
? VPN Server started on port 5000
? Real packet forwarding enabled with NAT
? IP packet parsing active
? Dynamic routing enabled
? SOCKS5 proxy support ready
? Auto-reconnect enabled
```

### **3.3 Verify Server Status**
Server dashboard should show:
- **Status:** ?? Server Running
- **Port:** 5000
- **Connected Clients:** 0
- **Total Connections:** 0

---

## ?? **STEP 4: START VPN CLIENT**

### **4.1 Launch Client Application**
1. **Open new Visual Studio instance** (important!)
2. Open the same solution in the new instance
3. Right-click **VPN.Client.UI** project
4. Select **"Set as Startup Project"**
5. Press **F5** or **Ctrl+F5**

### **4.2 Connect to VPN**
1. Client UI window opens
2. Enter a username (e.g., "test_user")
3. Click **"CONNECT TO VPN"** button
4. **IMPORTANT:** Go back to Server Dashboard

### **4.3 Approve User (First Time Only)**
1. Server Dashboard shows approval popup:
```
User Approval Required
????????????????????????
New user wants to connect:

Username: test_user
Client ID: client-abc12345
IP Address: 127.0.0.1

Do you want to accept this user?
    [Yes]        [No]
```
2. Click **"Yes"** to approve

### **4.4 Verify Connection Success**
**Client UI should show:**
```
? Connection established successfully
? Session ID: c81e2fc0-3f7d-4b51-ae92
? Encryption: AES-256 enabled
? Secure tunnel established
? SOCKS Proxy: Running on port 1080
```

**Server Dashboard should show:**
```
? User approved: test_user
? Encryption established, client authenticated
Client connected: client-abc12345 from 127.0.0.1
Connected Clients: 1
```

---

## ?? **STEP 5: VERIFY ENCRYPTION**

### **5.1 Check Encryption Logs**
In both Server Dashboard and Client UI logs, look for:

**Encryption Setup Messages:**
```
?? Session key established: A1B2C3D4...
? Encryption established, client authenticated
```

**Real-Time Encryption Messages:**
```
?? Decrypted packet: 128 ? 64 bytes
?? Encrypted response: 64 ? 128 bytes
```

### **5.2 Run Encryption Unit Tests**
```powershell
# Open PowerShell in project directory
cd "D:\Ali BSCS\third semester\Computer Networking\CN project\VPN-Solution"

# Run encryption tests
dotnet test VPN.Tests --filter "CryptoManager"
```

**Expected Test Output:**
```
?? Running CryptoManager Unit Tests...
? Encrypt/Decrypt round-trip test PASSED
? Multiple packets with different IVs test PASSED  
? HMAC tamper detection test PASSED
? Payload tamper detection test PASSED
? Empty payload test PASSED
? Cross-manager compatibility test PASSED
?? ALL TESTS PASSED! CryptoManager is working correctly.
```

---

## ?? **STEP 6: TEST REAL WEB TRAFFIC**

### **6.1 Configure Browser (Chrome)**
1. Open Chrome Settings
2. Go to **Advanced** ? **System** ? **Open proxy settings**
3. **Manual proxy setup:**
   - **SOCKS Host:** 127.0.0.1
   - **SOCKS Port:** 1080
   - **SOCKS Version:** 5
   - ?? **Enable "Proxy DNS when using SOCKS v5"**

### **6.2 Test Web Browsing**
1. With Chrome configured, browse to: https://www.google.com
2. **Watch Server Dashboard logs for:**
```
?? Extracted connection ID hash: 0x12345678, Data: 512 bytes
?? Decrypted packet: 532 ? 512 bytes
?? Sending response with hash: 0x12345678, Data: 1024 bytes  
?? Encrypted response: 1024 ? 1044 bytes
? Routed response: 1024 bytes to connection 0x12345678
```

3. **Watch Client UI logs for:**
```
?? Queued 512 bytes for connection conn-87654321
?? Routed 1024 bytes to connection conn-87654321
```

### **6.3 Verify Traffic Statistics**
**Client UI should show increasing:**
- **Upload Speed:** X KB/s
- **Download Speed:** Y KB/s  
- **Total Bytes Sent:** Z KB
- **Total Bytes Received:** W KB

---

## ?? **STEP 7: ADVANCED TESTING**

### **7.1 Test Multiple Connections**
1. Open multiple browser tabs
2. Browse different websites
3. Verify each connection gets unique ID in logs

### **7.2 Test Reconnection**
1. Click **"DISCONNECT"** in Client UI
2. Wait 5 seconds
3. Click **"RECONNECT"**
4. Should reconnect automatically (no approval needed)

### **7.3 Test DNS Resolution**
```powershell
# Test DNS through VPN (if DNS proxy is working)
nslookup google.com 127.0.0.1
```

### **7.4 Run Full Diagnostic**
```powershell
# Run comprehensive diagnostic
PowerShell -ExecutionPolicy Bypass -File "Quick-VPN-Diagnostic.ps1"
```

---

## ? **STEP 8: VERIFY ENCRYPTION IS WORKING**

### **8.1 Encryption Evidence Checklist**

**? Visual Confirmation:**
- [ ] Logs show "?? Session key established"
- [ ] Logs show "?? Encrypted response: X ? Y bytes"  
- [ ] Logs show "?? Decrypted packet: Y ? X bytes"
- [ ] Unit tests pass with "ALL TESTS PASSED"

**? Functional Confirmation:**
- [ ] Web browsing works through SOCKS proxy
- [ ] Multiple browser tabs work simultaneously
- [ ] Different websites load correctly
- [ ] Traffic statistics increase during browsing

**? Technical Confirmation:**
- [ ] Session key fingerprints are logged
- [ ] HMAC verification passes
- [ ] Connection-specific routing works
- [ ] No plaintext data visible in logs

### **8.2 Wireshark Verification (Advanced)**
If you have Wireshark installed:

1. **Capture VPN traffic:**
   ```
   tcp.port == 5000
   ```

2. **You should see:**
   - Encrypted TCP packets between client/server
   - No readable HTTP/HTTPS content
   - Binary encrypted payloads

3. **You should NOT see:**
   - Plaintext website content
   - Readable HTTP headers
   - Unencrypted DNS queries

---

## ?? **EXPECTED RESULTS**

### **Successful Test Indicators:**

**?? Encryption Working:**
- AES-256 encryption logs appear
- Unit tests pass 100%
- Web traffic flows through encrypted tunnel
- No plaintext visible in network capture

**?? Proxy Working:**
- Browser can access websites
- Traffic statistics show data transfer
- Multiple connections work simultaneously

**??? Security Working:**
- Session keys generated and logged
- HMAC verification passes
- Connection-specific routing functions
- User approval system works

---

## ?? **TROUBLESHOOTING**

### **Common Issues:**

**1. "Connection Failed"**
```bash
# Check firewall
netsh advfirewall firewall show rule name="VPN Server"

# Check if port is listening
netstat -an | findstr :5000
```

**2. "Proxy Connection Failed"**  
```bash
# Check VPN tunnel is active first
# Verify SOCKS proxy port
netstat -an | findstr :1080
```

**3. "Tests Failing"**
```bash
# Rebuild solution
dotnet clean
dotnet build

# Run tests with verbose output
dotnet test VPN.Tests --verbosity detailed
```

### **Get Help:**
If issues persist:
1. Check logs in both Server Dashboard and Client UI
2. Run the diagnostic script: `Quick-VPN-Diagnostic.ps1`
3. Verify Windows Firewall settings
4. Restart both server and client applications

---

## ?? **SUCCESS CONFIRMATION**

**Your VPN is working correctly when you see:**

1. ? **Server starts without errors**
2. ? **Client connects and gets approved**  
3. ? **Encryption logs appear in real-time**
4. ? **Unit tests pass 100%**
5. ? **Web browsing works through proxy**
6. ? **Traffic statistics increase during use**

**Congratulations! Your custom VPN with AES-256 encryption is fully operational!** ????

---
