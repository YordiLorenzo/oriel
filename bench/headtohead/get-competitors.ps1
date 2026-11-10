# Download the benchmark tools from their official sources, verify them, and unpack them into the benchmark folder:
# -Dir, default $env:ORIEL_BENCH_DIR, else C:\oriel-bench. -Tools picks which: oriel (the GitHub release, version
# -OrielVersion), cua (Cua Driver, version -CuaVersion), terminator (Terminator MCP 0.24.28) and agent-desktop
# (vercel-labs, version -AgentDesktopVersion).
# Rounds so far: 2026-10-21 (Cua Driver 0.38.0, Terminator 0.24.28), 2026-11-10 (oriel 0.1.0, Cua Driver 0.46.1, and
# in a second run agent-desktop 1.2.0).
param(
    [string]$Dir = $(if ($env:ORIEL_BENCH_DIR) { $env:ORIEL_BENCH_DIR } else { 'C:\oriel-bench' }),
    [string[]]$Tools = @('oriel', 'cua', 'terminator'),
    [string]$OrielVersion = '0.1.0',
    [string]$CuaVersion = '0.46.1',
    [string]$AgentDesktopVersion = '1.2.0'
)
$ErrorActionPreference = 'Stop'
$Tools = $Tools -split ','  # powershell -File passes "oriel,cua" as one string
$dl = "$Dir\downloads"
New-Item -ItemType Directory -Force $dl | Out-Null

# The hash a checksum list gives for the file whose name ends in $Suffix ("<sha256>  <name>" lines).
function Expected([string]$List, [string]$Suffix) {
    ((Get-Content $List) | Where-Object { $_ -match "$([regex]::Escape($Suffix))$" }) -split '\s+' | Select-Object -First 1
}
function Check([string]$Name, [string]$File, [string]$Want) {
    $got = (Get-FileHash $File -Algorithm SHA256).Hash.ToLower()
    "$Name sha256 expected $Want"
    "$Name sha256 got      $got"
    if (-not $Want -or $got -ne $Want.ToLower()) { throw "$Name checksum mismatch" }
}

if ($Tools -contains 'oriel') {
    $rel = "https://github.com/YordiLorenzo/oriel/releases/download/v$OrielVersion"
    New-Item -ItemType Directory -Force "$Dir\oriel" | Out-Null
    & curl.exe -sSfL -o "$dl\oriel-SHA256SUMS" "$rel/SHA256SUMS"
    & curl.exe -sSfL -o "$dl\oriel.exe" "$rel/oriel.exe"
    Check 'oriel' "$dl\oriel.exe" (Expected "$dl\oriel-SHA256SUMS" 'oriel.exe')
    Copy-Item -Force "$dl\oriel.exe" "$Dir\oriel\oriel.exe"
}
if ($Tools -contains 'cua') {
    $rel = "https://github.com/trycua/cua/releases/download/cua-driver-rs-v$CuaVersion"
    $zip = "cua-driver-rs-$CuaVersion-windows-x86_64-binary.zip"
    New-Item -ItemType Directory -Force "$Dir\cua-driver" | Out-Null
    & curl.exe -sSfL -o "$dl\cua-checksums.txt" "$rel/checksums.txt"
    & curl.exe -sSfL --limit-rate 18M -o "$dl\$zip" "$rel/$zip"
    Check 'cua' "$dl\$zip" (Expected "$dl\cua-checksums.txt" $zip)
    Expand-Archive -Force "$dl\$zip" "$Dir\cua-driver"
}
if ($Tools -contains 'terminator') {
    New-Item -ItemType Directory -Force "$Dir\terminator" | Out-Null
    & curl.exe -sSfL --limit-rate 18M -o "$dl\terminator.tgz" 'https://registry.npmjs.org/terminator-mcp-win32-x64-msvc/-/terminator-mcp-win32-x64-msvc-0.24.28.tgz'
    $sha512 = [Convert]::ToBase64String([Security.Cryptography.SHA512]::Create().ComputeHash([IO.File]::ReadAllBytes("$dl\terminator.tgz")))
    "terminator sha512 got sha512-$sha512"
    if ("sha512-$sha512" -ne 'sha512-eI69rpT1zkh42x1vgiOervgr9XrnCiuDNrhm6MxZYKk9kL2gRRStYmkhy10yJtO4trxrpzKzi00GgBLnfJieKQ==') { throw 'terminator integrity mismatch' }
    & tar.exe -xzf "$dl\terminator.tgz" -C "$Dir\terminator"
}
if ($Tools -contains 'agent-desktop') {
    $rel = "https://github.com/vercel-labs/agent-desktop/releases/download/v$AgentDesktopVersion"
    $tgz = "agent-desktop-v$AgentDesktopVersion-x86_64-pc-windows-msvc.tar.gz"
    New-Item -ItemType Directory -Force "$Dir\agent-desktop" | Out-Null
    & curl.exe -sSfL -o "$dl\agent-desktop-checksums.txt" "$rel/checksums.txt"
    & curl.exe -sSfL --limit-rate 18M -o "$dl\$tgz" "$rel/$tgz"
    Check 'agent-desktop' "$dl\$tgz" (Expected "$dl\agent-desktop-checksums.txt" $tgz)
    & tar.exe -xzf "$dl\$tgz" -C "$Dir\agent-desktop"
}
Get-ChildItem -Recurse "$Dir\oriel", "$Dir\cua-driver", "$Dir\terminator", "$Dir\agent-desktop" -Include *.exe -ErrorAction SilentlyContinue |
    ForEach-Object { "$($_.FullName)  $([math]::Round($_.Length/1MB,2)) MB  signed: $((Get-AuthenticodeSignature $_.FullName).Status)" }
