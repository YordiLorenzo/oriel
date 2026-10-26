# Install Cua Driver 0.38.0 in the WAA VM from the share (checksum-verified zip), turn its telemetry off before first
# use, then start its daemon and a loopback relay (port 3018) for its stdio MCP. Run in the desktop session, through
# WAA's /setup/execute. After a VM restart, rerun it (the install steps are idempotent).
$src = '\\host.lan\Data'
$dst = 'C:\agent-tools\cua-driver'
Start-Transcript "$src\cua-provision.log"
$want = ((Get-Content "$src\cua\checksums.txt") | Where-Object { $_ -match 'windows-x86_64-binary\.zip$' }) -split '\s+' | Select-Object -First 1
if ((Get-FileHash "$src\cua\cua.zip" -Algorithm SHA256).Hash -ne $want) { throw 'cua checksum mismatch' }
if (-not (Test-Path "$dst\cua-driver.exe")) {
    Expand-Archive -Force "$src\cua\cua.zip" $dst
    & "$dst\cua-driver.exe" telemetry disable
    & "$dst\cua-driver.exe" telemetry reset-id
}
Copy-Item "$src\mcp-relay.ps1" 'C:\agent-tools\mcp-relay.ps1' -Force
if (-not (Get-Process cua-driver -ErrorAction SilentlyContinue)) {
    Start-Process conhost.exe -ArgumentList "--headless $dst\cua-driver.exe serve --no-overlay"
}
if (-not (Get-NetTCPConnection -State Listen -LocalPort 3018 -ErrorAction SilentlyContinue)) {
    Start-Process conhost.exe -ArgumentList "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\agent-tools\mcp-relay.ps1 -Port 3018 -Exe $dst\cua-driver.exe -Arguments ""mcp --socket \\.\pipe\cua-driver"""
}
Start-Sleep 3
& "$dst\cua-driver.exe" telemetry status
Get-NetTCPConnection -State Listen -LocalPort 3018 -ErrorAction SilentlyContinue | Format-Table LocalAddress, LocalPort
Stop-Transcript
