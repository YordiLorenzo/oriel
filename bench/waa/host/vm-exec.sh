#!/bin/bash
# Run a PowerShell command in a WAA VM's desktop session through WAA's own /setup/execute endpoint (120 s limit).
# Usage: vm-exec.sh '<powershell>' [container]
body=$(python3 -c 'import json,sys; print(json.dumps({"command": ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", sys.argv[1]], "shell": False}))' "$1")
docker exec "${2:-winarena}" curl -s -m 130 -X POST -H 'Content-Type: application/json' 20.20.20.21:5000/setup/execute -d "$body" |
    python3 -c 'import json,sys; r=json.load(sys.stdin); print(r.get("output","") + r.get("error","") + r.get("message",""), end=""); sys.exit(r.get("returncode", 1))'
