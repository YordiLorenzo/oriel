#!/bin/bash
# WAA host on a DigitalOcean Intel droplet (nested KVM): Docker, WAA cloned and patched, images, Windows ISO, then the
# golden image build in the background (log: /root/waa-prepare.log).
set -eo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq docker.io git dos2unix python3 curl rsync >/dev/null
systemctl enable --now docker
cd /root
[ -d WindowsAgentArena ] || git clone -q https://github.com/microsoft/WindowsAgentArena.git
cd WindowsAgentArena
find scripts -maxdepth 1 -type f -exec dos2unix -q {} +
chmod +x scripts/*.sh
python3 /root/waa-patch.py /root/WindowsAgentArena
[ -f config.json ] || echo '{"OPENAI_API_KEY": "unused-placeholder"}' > config.json
cp /root/waa_env.py src/win-arena-container/client/
mkdir -p src/win-arena-container/vm/image
curl -sSL --retry 5 -o src/win-arena-container/vm/image/setup.iso 'https://go.microsoft.com/fwlink/?LinkId=2382600&clcid=0x409&culture=en-us&country=us' &
docker pull -q windowsarena/winarena-base:latest
wait
ls -l src/win-arena-container/vm/image/setup.iso
cd scripts && ./build-container-image.sh --mode dev 2>&1 | tail -3
nohup ./run-local.sh --mode dev --prepare-image true > /root/waa-prepare.log 2>&1 &
echo "setup done, golden image building"
