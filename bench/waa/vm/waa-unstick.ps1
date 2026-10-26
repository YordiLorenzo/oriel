# Unstick WAA's VM setup: its Caddy download (caddyserver.com's build API) hangs, and its pinned LibreOffice 24.8.2
# mirrors return 404. Put Caddy from its GitHub release in place, stop the stuck setup.ps1, and run setup.ps1 again
# (tools_config.json on the share now points LibreOffice at the TDF archive); setup skips what is already installed.
$src = '\\host.lan\Data'
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -like '*Data\setup.ps1*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Copy-Item "$src\caddy\caddy.exe" "$env:USERPROFILE\caddy_windows_amd64.exe" -Force
New-Item -ItemType File -Force (Split-Path $PROFILE) -ErrorAction SilentlyContinue | Out-Null
Add-Content $PROFILE "Set-Alias -Name caddy -Value `"$env:USERPROFILE\caddy_windows_amd64.exe`""
"rerun setup $(Get-Date -f s)" | Out-File -Append -Encoding ascii "$src\ps_script_log.txt"
powershell -ExecutionPolicy Bypass -File "$src\setup.ps1" *>> "$src\ps_script_log.txt"
"rerun done $(Get-Date -f s)" | Out-File -Append -Encoding ascii "$src\ps_script_log.txt"
