#!/bin/bash
# Prepare WindowsAgentArena in the "waa" WSL distro: Docker, the repo (patched, made executable) and the base image.
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
export DEBIAN_FRONTEND=noninteractive
printf '[boot]\nsystemd=true\n[user]\ndefault=root\n' > /etc/wsl.conf
apt-get update -qq
apt-get install -y -qq docker.io git dos2unix python3-pip python3-venv curl >/dev/null
systemctl enable --now docker 2>/dev/null || (dockerd >/var/log/dockerd.log 2>&1 &)
sleep 3
docker version --format 'docker {{.Server.Version}}'
cd /root
[ -d WindowsAgentArena ] || git clone -q https://github.com/microsoft/WindowsAgentArena.git
cd WindowsAgentArena && git log --oneline -1
find scripts -maxdepth 1 -type f -exec dos2unix -q {} +
chmod +x scripts/*.sh
python3 "$HERE/waa-patch.py" /root/WindowsAgentArena
mkdir -p src/win-arena-container/vm/image
echo "pulling windowsarena/winarena-base (about 18 GB)..."
docker pull -q windowsarena/winarena-base:latest
docker images --format '{{.Repository}}:{{.Tag}} {{.Size}}'
echo prep-done
