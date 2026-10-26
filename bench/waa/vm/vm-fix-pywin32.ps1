# WAA's server dies at "import win32ui" (DLL load failed: the MFC runtime is missing). Install Microsoft's VC++ runtime;
# if win32ui still won't load, pin pywin32 to 306. Then start WAA's logon task, which starts the server.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
& {
    python -m pip show pywin32 | Select-String Version
    $vc = "$env:TEMP\vc_redist.x64.exe"
    Invoke-WebRequest -UseBasicParsing https://aka.ms/vs/17/release/vc_redist.x64.exe -OutFile $vc
    (Get-AuthenticodeSignature $vc).Status
    Start-Process $vc -ArgumentList '/install', '/quiet', '/norestart' -Wait
    python -c "import win32ui; print('win32ui ok')"
    if ($LASTEXITCODE -ne 0) {
        python -m pip install pywin32==306
        python -c "import win32ui; print('win32ui ok after pin')"
    }
    Start-ScheduledTask WindowsArena_OnLogon
    Start-Sleep 15
    try { 'probe ' + (Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 http://127.0.0.1:5000/probe).StatusCode } catch { "probe failed: $_" }
} *> \\host.lan\Data\fix-pywin32.txt
