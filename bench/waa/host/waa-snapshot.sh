#!/bin/bash
# Restore or save an instance's VM disk with its container stopped.
# Usage: waa-snapshot.sh restore|save <snapshot dir> [repo dir] [container]
# restore copies <snapshot dir> over the instance's vm/storage; save copies vm/storage to <snapshot dir>. A relative
# snapshot dir is taken inside the instance's vm/ folder.
set -e
DIR=${3:-/root/WindowsAgentArena}
NAME=${4:-winarena}
cd "$DIR/src/win-arena-container/vm"
if docker ps --format '{{.Names}}' | grep -qx "$NAME"; then
    docker stop -t 180 "$NAME" >/dev/null
fi
for i in $(seq 1 60); do docker ps -a --format '{{.Names}}' | grep -qx "$NAME" || break; sleep 2; done
case "$1" in
    restore) rm -rf storage; cp -a --sparse=always "$2" storage ;;
    save) rm -rf "$2"; cp -a --sparse=always storage "$2" ;;
esac
du -sh storage "$2"
