#!/bin/bash
# Run a file command as root, for the files WAA's containers write as root (the VM disk, its TPM state, readable by
# root only, and the folders the VM writes into its share): directly when already root (a droplet), else in a
# throwaway container, which a user in the docker group may start (firecracker). Paths must lie under $WAA_HOME.
# Usage: as-root.sh <command> [args...]
H=${WAA_HOME:-/root}
[ "$(id -u)" = 0 ] && exec "$@"
exec docker run --rm -v "$H:$H" -w "$PWD" ubuntu:24.04 "$@"
