#!/bin/bash
# After the golden image build (do-setup.sh): save it as vm/storage-golden, boot it, provision it for both layers
# (OpenSSH with the VM key and oriel, from vm/oriel-provision.ps1; Cua Driver unpacked with its telemetry off, from
# vm/cua-provision.ps1), save that as vm/storage-provisioned, then build the warmed image (do-warm.sh, vm/storage-warm).
# The share must hold the tools first (waa-tools.sh). Usage: waa-provision.sh <oriel exe in $WAA_HOME, e.g. oriel-0.1.1.exe>
set -eo pipefail
H=${WAA_HOME:-/root}
B=$H/WindowsAgentArena
bash "$H/waa-snapshot.sh" save storage-golden "$B" winarena
setsid nohup "$H/waa-serve.sh" "$B" winarena 8006 3390 > "$H/waa-serve-base.log" 2>&1 < /dev/null &
for i in $(seq 1 120); do
    [ "$(docker exec winarena curl -s -m 3 -o /dev/null -w '%{http_code}' 20.20.20.21:5000/probe 2>/dev/null)" = 200 ] && break
    sleep 5
done
echo "server up after about $((i * 5)) s"
"$H/vm-exec.sh" '& \\host.lan\Data\oriel-provision.ps1' winarena
"$H/vm-exec.sh" '& \\host.lan\Data\cua-provision.ps1' winarena
bash "$H/waa-snapshot.sh" save storage-provisioned "$B" winarena
bash "$H/do-warm.sh" "$1"
