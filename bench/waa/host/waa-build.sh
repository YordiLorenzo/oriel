#!/bin/bash
# Build WAA's dev container on top of the pulled base image. run-local.sh insists on a model key even to
# prepare the VM image; the stock agent won't be used, so a placeholder satisfies the check.
set -eo pipefail
cd /root/WindowsAgentArena
[ -f config.json ] || echo '{"OPENAI_API_KEY": "unused-placeholder"}' > config.json
cd scripts
./build-container-image.sh --mode dev 2>&1 | tail -15
docker images --format '{{.Repository}}:{{.Tag}} {{.Size}}'
echo build-done
