# Start the benchmark competitors in the desktop session: Cua Driver's daemon on its default named pipe, Terminator's
# MCP server on loopback port 3017. -Tools picks which (cua, terminator). Nothing is registered to autostart. Run it
# from a PowerShell window on the PC's desktop (an SSH session has no desktop). -Dir is the benchmark folder that
# get-competitors.ps1 filled. Cua Driver's telemetry and its update check are off for these processes only.
param(
    [string]$Dir = $(if ($env:ORIEL_BENCH_DIR) { $env:ORIEL_BENCH_DIR } else { 'C:\oriel-bench' }),
    [string[]]$Tools = @('cua', 'terminator')
)
$Tools = $Tools -split ','  # powershell -File passes "cua,terminator" as one string
$env:CUA_DRIVER_RS_TELEMETRY_ENABLED = 'false'
$env:CUA_DRIVER_RS_UPDATE_CHECK = 'false'
$log = "$env:TEMP\bench-start.txt"
"start $(Get-Date -f s)" | Out-File $log
if ($Tools -contains 'cua' -and -not (Get-Process cua-driver -ErrorAction SilentlyContinue)) {
    Start-Process conhost.exe -ArgumentList "--headless `"$Dir\cua-driver\cua-driver.exe`" serve --no-overlay" -RedirectStandardError "$env:TEMP\cua-serve.err"
}
if ($Tools -contains 'terminator' -and -not (Get-NetTCPConnection -State Listen -LocalPort 3017 -ErrorAction SilentlyContinue)) {
    Start-Process conhost.exe -ArgumentList "--headless `"$Dir\terminator\package\terminator-mcp-agent.exe`" -t http -p 3017 --host 127.0.0.1"
}
Start-Sleep 4
Get-Process cua-driver, terminator-mcp-agent -ErrorAction SilentlyContinue | ForEach-Object { "$($_.ProcessName) pid $($_.Id) session $($_.SessionId) $($_.Path)" } | Out-File $log -Append
