# Simple VPN Diagnostic Script
param(
    [string]$ServerIP = "127.0.0.1",
    [int]$ServerPort = 5000,
    [int]$ProxyPort = 1080
)

Write-Host "=== VPN DIAGNOSTIC TOOL ===" -ForegroundColor Magenta
Write-Host ""

# Test 1: Network Connectivity
Write-Host "1. Testing Network Connectivity..." -ForegroundColor Yellow
try {
    $result = Test-NetConnection -ComputerName $ServerIP -Port $ServerPort -InformationLevel Quiet
    if ($result) {
        Write-Host "   ? Can reach $ServerIP`:$ServerPort" -ForegroundColor Green
    } else {
        Write-Host "   ? Cannot reach $ServerIP`:$ServerPort" -ForegroundColor Red
    }
} catch {
    Write-Host "   ? Network test failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 2: Port Status
Write-Host ""
Write-Host "2. Testing Port Status..." -ForegroundColor Yellow
$ports = @($ServerPort, $ProxyPort, 5353)
foreach ($port in $ports) {
    $listening = netstat -an | Select-String ":$port" | Select-String "LISTENING"
    if ($listening) {
        Write-Host "   ? Port $port is listening" -ForegroundColor Green
    } else {
        Write-Host "   ? Port $port is not listening" -ForegroundColor Red
    }
}

# Test 3: SOCKS5 Proxy
Write-Host ""
Write-Host "3. Testing SOCKS5 Proxy..." -ForegroundColor Yellow
try {
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    $connected = $tcpClient.ConnectAsync("127.0.0.1", $ProxyPort).Wait(3000)
    if ($connected) {
        Write-Host "   ? SOCKS5 proxy is accepting connections" -ForegroundColor Green
        $tcpClient.Close()
    } else {
        Write-Host "   ? SOCKS5 proxy connection timeout" -ForegroundColor Red
    }
} catch {
    Write-Host "   ? SOCKS5 proxy test failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 4: VPN Processes
Write-Host ""
Write-Host "4. Checking VPN Processes..." -ForegroundColor Yellow
$vpnProcesses = Get-Process | Where-Object {$_.ProcessName -like "*VPN*"}
if ($vpnProcesses.Count -gt 0) {
    Write-Host "   ? VPN processes found:" -ForegroundColor Green
    foreach ($proc in $vpnProcesses) {
        Write-Host "      - $($proc.ProcessName)" -ForegroundColor Green
    }
} else {
    Write-Host "   ??  No VPN processes found" -ForegroundColor Yellow
}

# Test 5: Configuration Files
Write-Host ""
Write-Host "5. Checking Configuration Files..." -ForegroundColor Yellow
$configPath = "$env:APPDATA\VPN-Solution"
if (Test-Path $configPath) {
    Write-Host "   ? Config directory exists: $configPath" -ForegroundColor Green
    $configFiles = Get-ChildItem "$configPath\*.json" -ErrorAction SilentlyContinue
    if ($configFiles) {
        foreach ($file in $configFiles) {
            Write-Host "      - $($file.Name)" -ForegroundColor Green
        }
    }
} else {
    Write-Host "   ??  Config directory not found: $configPath" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== TROUBLESHOOTING TIPS ===" -ForegroundColor Magenta
Write-Host ""
Write-Host "If VPN Server port is not listening:" -ForegroundColor Cyan
Write-Host "  - Start VPN Server Dashboard" -ForegroundColor White
Write-Host "  - Click 'START SERVER'" -ForegroundColor White
Write-Host ""
Write-Host "If SOCKS proxy is not working:" -ForegroundColor Cyan
Write-Host "  - Ensure VPN client is connected" -ForegroundColor White
Write-Host "  - Configure browser: SOCKS5 proxy 127.0.0.1:$ProxyPort" -ForegroundColor White
Write-Host ""
Write-Host "Browser Configuration (Chrome):" -ForegroundColor Cyan
Write-Host "  1. Settings > Advanced > System" -ForegroundColor White
Write-Host "  2. Open proxy settings" -ForegroundColor White
Write-Host "  3. Manual setup: SOCKS Host=127.0.0.1, Port=$ProxyPort" -ForegroundColor White
Write-Host ""
Write-Host "Firewall Rule:" -ForegroundColor Cyan
Write-Host "  netsh advfirewall firewall add rule name=`"VPN Server`" dir=in action=allow protocol=TCP localport=$ServerPort" -ForegroundColor Green
Write-Host ""

Write-Host "Diagnostic completed at $(Get-Date)" -ForegroundColor Gray