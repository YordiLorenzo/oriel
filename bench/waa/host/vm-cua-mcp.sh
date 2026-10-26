#!/bin/bash
# Cua Driver's MCP server in a WAA VM: its stdio proxy runs in the VM's desktop session behind a loopback relay on
# port 3018 (vm/cua-provision.ps1; the daemon refuses Session 0 clients such as an SSH session), and ssh -W carries
# stdio to that port. Usage: vm-cua-mcp.sh [container]
IP=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "${1:-winarena}")
exec ssh -i /root/.ssh/waa_vm -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o LogLevel=ERROR -W 127.0.0.1:3018 "Docker@$IP"
