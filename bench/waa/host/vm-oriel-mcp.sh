#!/bin/bash
# oriel's MCP server in a WAA VM: SSH to the container's address (dockur forwards it to the VM) and run "oriel mcp",
# which talks to the oriel daemon in the VM's desktop session. bench/waa/run.py starts this through ssh + wsl.
# Usage: vm-oriel-mcp.sh [container]
IP=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "${1:-winarena}")
exec ssh -i /root/.ssh/waa_vm -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR -T "Docker@$IP" \
    'C:\Users\Docker\AppData\Local\Programs\oriel\oriel.exe mcp'
