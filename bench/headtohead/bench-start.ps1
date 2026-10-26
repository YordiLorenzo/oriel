# Start the benchmark competitors in the desktop session: Cua Driver's daemon on its default named pipe, Terminator's
# MCP server on loopback port 3017. Nothing is registered to autostart. Run it from a PowerShell window on the PC's
# desktop (an SSH session has no desktop). -Dir is the benchmark folder that get-competitors.ps1 filled.
param([string]$Dir = $(if ($env:ORIEL_BENCH_DIR) { $env:ORIEL_BENCH_DIR } else { 'C:\oriel-bench' }))
$log = "$env:TEMP\bench-start.txt"
"start $(Get-Date -f s)" | Out-File $log
if (-not (Get-Process cua-driver -ErrorAction SilentlyContinue)) {
    Start-Process conhost.exe -ArgumentList "--headless `"$Dir\cua-driver\cua-driver.exe`" serve --no-overlay" -RedirectStandardError "$env:TEMP\cua-serve.err"
}
if (-not (Get-NetTCPConnection -State Listen -LocalPort 3017 -ErrorAction SilentlyContinue)) {
    Start-Process conhost.exe -ArgumentList "--headless `"$Dir\terminator\package\terminator-mcp-agent.exe`" -t http -p 3017 --host 127.0.0.1"
}
Start-Sleep 4
Get-Process cua-driver, terminator-mcp-agent -ErrorAction SilentlyContinue | ForEach-Object { "$($_.ProcessName) pid $($_.Id) session $($_.SessionId)" } | Out-File $log -Append
