# oriel-deploy.ps1: put a freshly built oriel.exe in the benchmark folder, stopping the daemon that runs from it
# (the next MCP call starts the new one).
#   -Dir     the benchmark folder (default $env:ORIEL_BENCH_DIR, else C:\oriel-bench); the exe goes to <Dir>\oriel
#   -Source  the built exe (default: dist\oriel.exe in the checkout this script sits in)
param(
    [string]$Dir = $(if ($env:ORIEL_BENCH_DIR) { $env:ORIEL_BENCH_DIR } else { 'C:\oriel-bench' }),
    [string]$Source = (Join-Path $PSScriptRoot '..\..\dist\oriel.exe')
)
$Source = (Resolve-Path $Source).Path
$bench = Join-Path $Dir 'oriel\oriel.exe'
New-Item -ItemType Directory -Force (Split-Path $bench) | Out-Null
Get-Process oriel -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $bench -or $_.Path -eq $Source } | Stop-Process -Force
Start-Sleep -Milliseconds 500
Copy-Item -Force $Source $bench
"deployed $((Get-Item $bench).LastWriteTime) $((Get-FileHash $bench).Hash.Substring(0,12))"
