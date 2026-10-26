# Start mcp-relay.ps1 (next to this script) for Cua Driver's stdio MCP on loopback port 3018, in the desktop session:
# run it from a PowerShell window on the PC's desktop. -Dir is the benchmark folder that get-competitors.ps1 filled.
param([string]$Dir = $(if ($env:ORIEL_BENCH_DIR) { $env:ORIEL_BENCH_DIR } else { 'C:\oriel-bench' }))
if (-not (Get-NetTCPConnection -State Listen -LocalPort 3018 -ErrorAction SilentlyContinue)) {
    Start-Process conhost.exe -ArgumentList "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$PSScriptRoot\mcp-relay.ps1`" -Port 3018 -Exe `"$Dir\cua-driver\cua-driver.exe`" -Arguments `"mcp --socket \\.\pipe\cua-driver`""
}
