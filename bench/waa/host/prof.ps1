# Time oriel's actions stage by stage (Trace) on the apps where WindowsAgentArena showed them slow. Runs in a WAA VM
# over SSH, against the oriel daemon in the desktop session. Usage: powershell -File prof.ps1 [oriel.exe]
param([string]$exe = 'C:\Users\Docker\AppData\Local\Programs\oriel\oriel.exe')

function O {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $reply = (& $exe call @args --Trace true 2>&1 | Out-String).Trim() -split "`r?`n"
    $trace = ($reply | Where-Object { $_ -match '^\(trace:' }) -join ' '
    '{0,6} ms  {1,-60} {2,5} lines  {3}' -f $sw.ElapsedMilliseconds, (($args -join ' ') -replace '\s+', ' ').Substring(0, [Math]::Min(60, ($args -join ' ').Length)), $reply.Count, $trace
}

$code = "$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe"

O tree --Window Desktop
O tree --Window Desktop
O launch --Path explorer.exe --Args C:\Users\Docker\Documents --WaitTitle Documents
O tree --Window Documents
O find --Window Documents --Name Desktop
O keys --Window Documents --Keys '{DOWN}'
O keys --Window Documents --Keys '{DOWN}'
O invoke --Window Documents --Name View
O keys --Window Documents --Keys '{ESC}'
O invoke --Window Documents --Name Desktop

O launch --Path notepad.exe --WaitTitle Notepad
O keys --Window Notepad --Keys 'hello world'
O find --Window Notepad --Name File
O find --Window Notepad --Name Edit
O invoke --Window Notepad --Name File
O keys --Window Notepad --Keys '{ESC}'

O launch --Path calc.exe --WaitTitle Calculator
O keys --Window Calculator --Keys '12+34='
O invoke --Window Calculator --Name Seven
O find --Window Calculator --Name Display

if (Test-Path $code) {
    O launch --Path $code --WaitTitle 'Visual Studio Code' --Timeout 40
    O find --Window 'Visual Studio Code' --Name File
    O invoke --Window 'Visual Studio Code' --Name File
    O keys --Window 'Visual Studio Code' --Keys '{ESC}'
    O keys --Window 'Visual Studio Code' --Keys '^n'
    O keys --Window 'Visual Studio Code' --Keys 'hello'
}
