#!/bin/bash
# WAA host on a Linux machine with KVM (a DigitalOcean droplet as root, or the lab's firecracker as a user in the
# docker group): Docker, WAA cloned and patched, images, Windows ISO, the VM key, then the golden image build in the
# background (log: $WAA_HOME/waa-prepare.log). Everything lives in $WAA_HOME (default /root), next to this script's
# copies. Downloads are capped at about 18 MB/s (the lab's Starlink is shared).
set -eo pipefail
H=${WAA_HOME:-/root}
if [ "$(id -u)" = 0 ]; then
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y -qq docker.io git python3 curl rsync >/dev/null
    systemctl enable --now docker
fi
cd "$H"
[ -d WindowsAgentArena ] || git clone -q https://github.com/microsoft/WindowsAgentArena.git
cd WindowsAgentArena
git log -1 --format='WAA %h %ci'
find scripts -maxdepth 1 -type f -exec sed -i 's/\r$//' {} +
chmod +x scripts/*.sh
python3 "$H/waa-patch.py" "$H/WindowsAgentArena"
[ -f config.json ] || echo '{"OPENAI_API_KEY": "unused-placeholder"}' > config.json
cp "$H/waa_env.py" src/win-arena-container/client/
[ -f "$H/.ssh/waa_vm" ] || { mkdir -p "$H/.ssh"; ssh-keygen -q -t ed25519 -N '' -C waa_vm -f "$H/.ssh/waa_vm"; }
cp "$H/.ssh/waa_vm.pub" src/win-arena-container/vm/setup/waa_vm.pub
mkdir -p src/win-arena-container/vm/image
ISO=src/win-arena-container/vm/image/setup.iso
[ -s $ISO ] || { curl -sSfL --retry 5 -C - --limit-rate 18M -o $ISO.part 'https://go.microsoft.com/fwlink/?LinkId=2382600&clcid=0x409&culture=en-us&country=us' && mv $ISO.part $ISO; } &
# The base image through skopeo in a container whose TCP receive window caps it at about 165 Mbit/s.
docker image inspect windowsarena/winarena-base:latest >/dev/null 2>&1 ||
    docker run --rm --sysctl net.ipv4.tcp_rmem="4096 131072 800000" -v /var/run/docker.sock:/var/run/docker.sock \
        quay.io/skopeo/stable:v1.16.1 copy -q docker://windowsarena/winarena-base:latest docker-daemon:windowsarena/winarena-base:latest
wait
ls -l $ISO
cd scripts && ./build-container-image.sh --mode dev 2>&1 | tail -3
setsid nohup ./run-local.sh --mode dev --prepare-image true > "$H/waa-prepare.log" 2>&1 < /dev/null &
echo "setup done, golden image building"
