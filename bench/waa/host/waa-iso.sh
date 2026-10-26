#!/bin/bash
# Download the Windows 11 Enterprise evaluation ISO (en-US, 64-bit) from Microsoft's evaluation center.
set -e
df -h / | tail -1
cd /root
curl -sSL --retry 5 -C - -o win11-eval.iso 'https://go.microsoft.com/fwlink/?LinkId=2382600&clcid=0x409&culture=en-us&country=us'
ls -l win11-eval.iso
sha256sum win11-eval.iso
echo iso-done
