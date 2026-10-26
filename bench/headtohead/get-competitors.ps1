# Download Cua Driver 0.38.0 and Terminator MCP 0.24.28 (official sources), verify them, and unpack them into the
# benchmark folder: -Dir, default $env:ORIEL_BENCH_DIR, else C:\oriel-bench.
param([string]$Dir = $(if ($env:ORIEL_BENCH_DIR) { $env:ORIEL_BENCH_DIR } else { 'C:\oriel-bench' }))
$ErrorActionPreference = 'Stop'
$dl = "$Dir\downloads"
New-Item -ItemType Directory -Force $dl, "$Dir\cua-driver", "$Dir\terminator" | Out-Null
$cua = 'https://github.com/trycua/cua/releases/download/cua-driver-rs-v0.38.0'
& curl.exe -sSL --limit-rate 20M -o "$dl\cua.zip" "$cua/cua-driver-rs-0.38.0-windows-x86_64-binary.zip"
& curl.exe -sSL -o "$dl\checksums.txt" "$cua/checksums.txt"
$want = ((Get-Content "$dl\checksums.txt") | Where-Object { $_ -match 'windows-x86_64-binary\.zip$' }) -split '\s+' | Select-Object -First 1
$got = (Get-FileHash "$dl\cua.zip" -Algorithm SHA256).Hash
"cua sha256 expected $want"
"cua sha256 got      $($got.ToLower())"
if ($got -ne $want) { throw 'cua checksum mismatch' }
Expand-Archive -Force "$dl\cua.zip" "$Dir\cua-driver"
& curl.exe -sSL --limit-rate 20M -o "$dl\terminator.tgz" 'https://registry.npmjs.org/terminator-mcp-win32-x64-msvc/-/terminator-mcp-win32-x64-msvc-0.24.28.tgz'
$sha512 = [Convert]::ToBase64String([Security.Cryptography.SHA512]::Create().ComputeHash([IO.File]::ReadAllBytes("$dl\terminator.tgz")))
"terminator sha512 got sha512-$sha512"
if ("sha512-$sha512" -ne 'sha512-eI69rpT1zkh42x1vgiOervgr9XrnCiuDNrhm6MxZYKk9kL2gRRStYmkhy10yJtO4trxrpzKzi00GgBLnfJieKQ==') { throw 'terminator integrity mismatch' }
& tar.exe -xzf "$dl\terminator.tgz" -C "$Dir\terminator"
Get-ChildItem -Recurse "$Dir\cua-driver", "$Dir\terminator" -Include *.exe | ForEach-Object { "$($_.FullName)  $([math]::Round($_.Length/1MB,1)) MB  signed: $((Get-AuthenticodeSignature $_.FullName).Status)" }
