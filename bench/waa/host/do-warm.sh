#!/bin/bash
# Build the warmed WAA image: the provisioned disk plus one oriel build, with LibreOffice started once (Calc, then
# Writer) so its first-run updater has finished before any task. Saved as vm/storage-warm.
# Usage: do-warm.sh [oriel exe in $WAA_HOME, default oriel-v4.exe]
set -e
H=${WAA_HOME:-/root}
O_EXE=${1:-oriel-v4.exe}
B=$H/WindowsAgentArena
V=$B/src/win-arena-container/vm
docker ps --format '{{.Names}}' | grep '^waa' | xargs -r -n1 -P14 docker stop -t 120 >/dev/null
bash "$H/waa-snapshot.sh" restore storage-provisioned $B winarena
cp "$H/$O_EXE" "$V/setup/$O_EXE"
nohup "$H/waa-serve.sh" $B winarena 8006 3390 > "$H/waa-serve-base.log" 2>&1 &
for i in $(seq 1 90); do [ "$(docker exec winarena curl -s -m 3 -o /dev/null -w '%{http_code}' 20.20.20.21:5000/probe 2>/dev/null)" = 200 ] && break; sleep 5; done
"$H/vm-exec.sh" "Copy-Item \\\\host.lan\\Data\\$O_EXE C:\\agent-tools\\oriel.exe -Force; Start-Process C:\\agent-tools\\oriel.exe -ArgumentList 'install --autostart' -WindowStyle Hidden; Start-Sleep 8" winarena >/dev/null
IP=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' winarena)
O() { timeout 60 ssh -i "$H/.ssh/waa_vm" -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR "Docker@$IP" "C:\\Users\\Docker\\AppData\\Local\\Programs\\oriel\\oriel.exe call $*" 2>&1; }
for app in calc writer; do
    want=$([ $app = calc ] && echo "LibreOffice Calc" || echo "LibreOffice Writer")
    "$H/vm-exec.sh" "Start-Process 'C:\Program Files\LibreOffice\program\soffice.exe' -ArgumentList '--$app'" winarena >/dev/null
    for i in $(seq 1 120); do
        w=$(O windows)
        if echo "$w" | grep -q "$want" && ! echo "$w" | grep -q "LibreOffice Update"; then echo "$app ready after $((i*5))s"; break; fi
        sleep 5
    done
    echo "$w" | grep -i 'libreoffice\|tip of the day' || { echo "$app not ready after 120 checks; windows: $w"; exit 1; }
    sleep 5
    O close --Window "\"$want\"" || true
    sleep 5
done
echo "windows left:"; O windows
bash "$H/waa-snapshot.sh" save storage-warm $B winarena
echo warm-done
