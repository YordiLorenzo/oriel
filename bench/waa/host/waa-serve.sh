#!/bin/bash
# Run a WAA container with the VM up and no stock agent, for runs driven from outside (bench/waa/run.py).
# Usage: waa-serve.sh [repo dir] [container] [browser port] [rdp port]   (defaults: the first instance)
# The VM's size comes from WAA_RAM (default 8G) and WAA_CPUS (default 8).
DIR=${1:-/root/WindowsAgentArena}
cd "$DIR/scripts"
echo "start $(date -Is)"
./run-local.sh --mode dev --skip-build true --start-client false --container-name "${2:-winarena}" \
    --browser-port "${3:-8006}" --rdp-port "${4:-3390}" --ram-size "${WAA_RAM:-8G}" --cpu-cores "${WAA_CPUS:-8}"
echo "exit $? $(date -Is)"
