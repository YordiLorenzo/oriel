# Start mcp-relay.ps1 (next to this script) for a stdio MCP server on loopback port -Port, in the desktop session: run
# it from a PowerShell window on the PC's desktop. -Dir is the benchmark folder that get-competitors.ps1 filled.
# -Tool cua (default, port 3018): the relayed "cua-driver mcp" talks to the daemon that bench-start.ps1 started; with
# -Direct it is bare "cua-driver mcp", which on Windows owns its runtime (Cua Driver 0.46 and later). Cua Driver's
# telemetry and its update check are off for the relayed processes only.
# -Tool agent-desktop (port 3020): "agent-desktop mcp", with its state root in -Dir\agent-desktop\state, so its
# snapshots stay in the benchmark folder.
param(
    [string]$Dir = $(if ($env:ORIEL_BENCH_DIR) { $env:ORIEL_BENCH_DIR } else { 'C:\oriel-bench' }),
    [ValidateSet('cua', 'agent-desktop')][string]$Tool = 'cua',
    [int]$Port = $(if ($Tool -eq 'agent-desktop') { 3020 } else { 3018 }),
    [switch]$Direct
)
if ($Tool -eq 'agent-desktop') {
    $env:AGENT_DESKTOP_HOME = "$Dir\agent-desktop\state"
    $exe, $mcp = "$Dir\agent-desktop\agent-desktop.exe", 'mcp'
} else {
    $env:CUA_DRIVER_RS_TELEMETRY_ENABLED = 'false'
    $env:CUA_DRIVER_RS_UPDATE_CHECK = 'false'
    $exe = "$Dir\cua-driver\cua-driver.exe"
    $mcp = if ($Direct) { 'mcp' } else { 'mcp --socket \\.\pipe\cua-driver' }
}
if (-not (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)) {
    Start-Process conhost.exe -ArgumentList "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$PSScriptRoot\mcp-relay.ps1`" -Port $Port -Exe `"$exe`" -Arguments `"$mcp`""
}
