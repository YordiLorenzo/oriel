# Start mcp-relay.ps1 (next to this script) for Cua Driver's stdio MCP on loopback port -Port (default 3018), in the
# desktop session: run it from a PowerShell window on the PC's desktop. -Dir is the benchmark folder that
# get-competitors.ps1 filled. By default the relayed "cua-driver mcp" talks to the daemon that bench-start.ps1
# started; with -Direct it is bare "cua-driver mcp", which on Windows owns its runtime (Cua Driver 0.46 and later).
# Cua Driver's telemetry and its update check are off for the relayed processes only.
param(
    [string]$Dir = $(if ($env:ORIEL_BENCH_DIR) { $env:ORIEL_BENCH_DIR } else { 'C:\oriel-bench' }),
    [int]$Port = 3018,
    [switch]$Direct
)
$env:CUA_DRIVER_RS_TELEMETRY_ENABLED = 'false'
$env:CUA_DRIVER_RS_UPDATE_CHECK = 'false'
$mcp = if ($Direct) { 'mcp' } else { 'mcp --socket \\.\pipe\cua-driver' }
if (-not (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)) {
    Start-Process conhost.exe -ArgumentList "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$PSScriptRoot\mcp-relay.ps1`" -Port $Port -Exe `"$Dir\cua-driver\cua-driver.exe`" -Arguments `"$mcp`""
}
