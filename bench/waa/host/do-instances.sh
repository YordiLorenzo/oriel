#!/bin/bash
# Make and boot WAA instances i..j on the droplet from one VM disk: each gets its own folder (/root/waa-<i>),
# container (waa<i>), ports and a copy of the disk. Usage: do-instances.sh <first> <last> <disk dir>
set -e
BASE=/root/WindowsAgentArena
for i in $(seq "$1" "$2"); do
    D=/root/waa-$i
    rsync -a --delete --exclude 'src/win-arena-container/vm/storage*' --exclude 'src/win-arena-container/vm/image/setup.iso' "$BASE/" "$D/"
    docker rm -f "waa$i" >/dev/null 2>&1 || true
    rm -rf "$D/src/win-arena-container/vm/storage"
    cp -a --sparse=always "$3" "$D/src/win-arena-container/vm/storage"
    WAA_RAM=8G WAA_CPUS=4 nohup /root/waa-serve.sh "$D" "waa$i" $((8100 + i)) $((3500 + i)) > "/root/waa-serve-$i.log" 2>&1 &
done
