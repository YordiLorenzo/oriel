# Open the two read-only browser windows the head-to-head reads, Chrome and Edge, on one static page. Each runs as a
# guest, in a new, empty profile folder under -Dir\browsers. Edge also needs --disable-features=msImplicitSignin: on
# 2026-11-10 it signed the new profile in to the Windows account by itself within a minute, with --inprivate and with
# --guest alike, and with this flag it did not. Run this in the desktop session (an SSH session has no desktop), and
# browsers-check.ps1 over SSH before and after the run.
param(
    [string]$Dir = $(if ($env:ORIEL_BENCH_DIR) { $env:ORIEL_BENCH_DIR } else { 'C:\oriel-bench' }),
    [string]$Url = 'https://en.wikipedia.org/wiki/Microsoft_Windows'
)
# Keep a page behind other windows rendering and exposing its tree.
$flags = @('--no-first-run', '--no-default-browser-check', '--disable-backgrounding-occluded-windows',
    '--disable-renderer-backgrounding', '--disable-background-timer-throttling')
$chrome = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
Start-Process $chrome -ArgumentList (@("--user-data-dir=$Dir\browsers\chrome", '--guest') + $flags + $Url)
Start-Process $edge -ArgumentList (@("--user-data-dir=$Dir\browsers\edge", '--guest', '--disable-features=msImplicitSignin') + $flags + $Url)
