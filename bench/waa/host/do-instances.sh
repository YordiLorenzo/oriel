#!/bin/bash
# Make and boot WAA instances i..j from one VM disk: each gets its own folder ($WAA_HOME/waa-<i>, default /root),
# container (waa<i>), ports and a copy of the disk. Usage: do-instances.sh <first> <last> <disk dir>
set -e
H=${WAA_HOME:-/root}
BASE=$H/WindowsAgentArena
for i in $(seq "$1" "$2"); do
    D=$H/waa-$i
    rsync -a --delete --exclude 'src/win-arena-container/vm/storage*' --exclude 'src/win-arena-container/vm/image/setup.iso' "$BASE/" "$D/"
    docker rm -f "waa$i" >/dev/null 2>&1 || true
    rm -rf "$D/src/win-arena-container/vm/storage"
    cp -a --sparse=always "$3" "$D/src/win-arena-container/vm/storage"
    WAA_RAM=8G WAA_CPUS=4 nohup "$H/waa-serve.sh" "$D" "waa$i" $((8100 + i)) $((3500 + i)) > "$H/waa-serve-$i.log" 2>&1 &
done
