#!/bin/bash
# Put the benchmark's tools in WAA's share (vm/setup, \\host.lan\Data in the VM), each checked against the hash its
# release publishes: oriel (oriel.exe, and oriel-<version>.exe for do-roles.sh and do-warm.sh), Cua Driver (cua/cua.zip
# and its checksums.txt, which cua-provision.ps1 checks again), the OpenSSH MSI (GitHub's asset digest; the VM also
# shows its Authenticode signature) and the in-VM scripts from this repo. do-setup.sh put the VM's key there.
# Usage: waa-tools.sh <oriel version> <cua driver version>   (round 10: waa-tools.sh 0.1.1 0.46.1)
set -eo pipefail
H=${WAA_HOME:-/root}
BENCH=$(cd "$(dirname "$0")/.." && pwd)
S=$H/WindowsAgentArena/src/win-arena-container/vm/setup
OPENSSH=https://github.com/PowerShell/Win32-OpenSSH/releases/download/10.0.0.0p2-Preview/OpenSSH-Win64-v10.0.0.0.msi
OPENSSH_SHA256=ddec9c53864280759cf9f74791cefd387100e3946aa849a1c138a4ed1b96b7d9
fetch() { curl -sSfL --retry 3 --limit-rate 18M -o "$1" "$2"; }
check() {  # <file> <expected sha256> <name>
    got=$(sha256sum "$1" | cut -d' ' -f1)
    [ -n "$2" ] && [ "$got" = "$2" ] || { echo "$3: sha256 $got, expected '$2'" >&2; exit 1; }
    echo "$3 sha256 $got ok"
}
listed() { awk -v n="$2" '$2 == n || $2 == "*" n { print $1 }' "$1"; }  # <checksum list> <file name>

rel=https://github.com/YordiLorenzo/oriel/releases/download/v$1
fetch "$S/oriel-SHA256SUMS" "$rel/SHA256SUMS"
fetch "$S/oriel.exe" "$rel/oriel.exe"
check "$S/oriel.exe" "$(listed "$S/oriel-SHA256SUMS" oriel.exe)" "oriel $1"
cp "$S/oriel.exe" "$S/oriel-$1.exe"
cp "$S/oriel.exe" "$H/oriel-$1.exe"

zip=cua-driver-rs-$2-windows-x86_64-binary.zip
mkdir -p "$S/cua"
fetch "$S/cua/checksums.txt" "https://github.com/trycua/cua/releases/download/cua-driver-rs-v$2/checksums.txt"
fetch "$S/cua/cua.zip" "https://github.com/trycua/cua/releases/download/cua-driver-rs-v$2/$zip"
check "$S/cua/cua.zip" "$(listed "$S/cua/checksums.txt" "$zip")" "Cua Driver $2"

[ -f "$S/OpenSSH-Win64-v10.0.0.0.msi" ] || fetch "$S/OpenSSH-Win64-v10.0.0.0.msi" "$OPENSSH"
check "$S/OpenSSH-Win64-v10.0.0.0.msi" "$OPENSSH_SHA256" "OpenSSH 10.0.0.0"

cp "$BENCH/vm/oriel-provision.ps1" "$BENCH/vm/cua-provision.ps1" "$BENCH/../headtohead/mcp-relay.ps1" "$S/"
ls -l "$S"/oriel*.exe "$S"/cua/* "$S"/*.msi | awk '{print $5, $NF}'
