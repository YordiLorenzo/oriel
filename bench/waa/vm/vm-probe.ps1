# Diagnose WAA's in-VM server: is python/caddy running, what listens, did the logon task run, can it be reached locally.
& {
    Get-CimInstance Win32_Process -Filter "name like '%python%' or name like '%caddy%'" | Format-Table ProcessId, Name, CommandLine -AutoSize -Wrap
    Get-NetTCPConnection -State Listen | Where-Object LocalPort -in 5000, 9222, 1337 | Format-Table LocalAddress, LocalPort, OwningProcess
    Get-ScheduledTaskInfo WindowsArena_OnLogon | Format-List LastRunTime, LastTaskResult
    ipconfig | findstr IPv4
    try { (Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 http://127.0.0.1:5000/probe).StatusCode } catch { "local probe: $_" }
    Get-NetFirewallProfile | Format-Table Name, Enabled
} *> \\host.lan\Data\probe.txt
