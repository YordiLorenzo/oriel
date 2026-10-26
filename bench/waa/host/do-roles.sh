#!/bin/bash
# After do-instances.sh: wait for each instance's VM, then set it up for its tool layer (the disk has oriel installed
# and Cua Driver unpacked; oriel-vN swaps in oriel-vN.exe from the share). Usage: do-roles.sh <oriel|oriel-vN|oriel-shell|cua> <first> <last>
L=$1
for i in $(seq "$2" "$3"); do
    C=waa$i
    for t in $(seq 1 120); do
        [ "$(docker exec "$C" curl -s -m 3 -o /dev/null -w '%{http_code}' 20.20.20.21:5000/probe 2>/dev/null)" = 200 ] && break
        sleep 5
    done
    # Every role: Windows Update, its orchestrator, indexing and SysMain off. Freshly restored VMs ran them at
    # 200-250% CPU each, which slowed every layer's calls several times over.
    /root/vm-exec.sh "foreach (\$s in 'wuauserv','UsoSvc','WSearch','SysMain') { sc.exe config \$s start= disabled | Out-Null; sc.exe stop \$s | Out-Null }" "$C" >/dev/null
    case $L in
        oriel-shell) /root/vm-exec.sh 'New-Item -ItemType Directory -Force $env:LOCALAPPDATA\oriel | Out-Null; Set-Content -Encoding ascii $env:LOCALAPPDATA\oriel\config.json "{""shell"": true}"; Start-Process C:\agent-tools\oriel.exe -ArgumentList "install --autostart" -WindowStyle Hidden; Start-Sleep 6' "$C" >/dev/null ;;
        oriel-v*) /root/vm-exec.sh "Copy-Item \\\\host.lan\\Data\\$L.exe C:\\agent-tools\\oriel.exe -Force; Start-Process C:\\agent-tools\\oriel.exe -ArgumentList 'install --autostart' -WindowStyle Hidden; Start-Sleep 8" "$C" >/dev/null ;;
        cua) /root/vm-exec.sh '& C:\agent-tools\oriel.exe uninstall; & \\host.lan\Data\cua-provision.ps1' "$C" >/dev/null ;;
    esac
    echo "$C ready for $L"
done
