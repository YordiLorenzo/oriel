# build.ps1: build the demo app, demo\bin\oriel-demo.exe, with the C# compiler that comes with the .NET Framework (the
# same csc.exe that build\build.ps1 uses), so no SDK is needed.
#   powershell -ExecutionPolicy Bypass -File demo\build.ps1 [-Out <folder>]
param([string]$Out = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
$fx = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Force $Out | Out-Null
$exe = Join-Path $Out 'oriel-demo.exe'
$refs = 'System.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { "/r:$(Join-Path $fx $_)" }
& (Join-Path $fx 'csc.exe') /nologo /target:winexe /optimize+ /warn:4 "/out:$exe" @refs (Join-Path $PSScriptRoot 'SignUpApp.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
"built $exe ($([math]::Round((Get-Item $exe).Length / 1KB)) KB)"
