#!/bin/bash
# Build WAA's golden Windows 11 VM image (unattended install from setup.iso, then WAA's app setup).
# Started from Windows through WMI so the wsl.exe that keeps this distro alive outlives the SSH session.
cd /root/WindowsAgentArena/scripts
echo "start $(date -Is)"
./run-local.sh --mode dev --prepare-image true
echo "exit $? $(date -Is)"
