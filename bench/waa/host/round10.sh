#!/bin/bash
# Round 10's full run on the WAA host (firecracker): for each run, rebuild the 10 VMs from the warmed image, set up
# waa0-4 for oriel 0.1.1 and waa5-9 for Cua Driver 0.46.1, and run both layers side by side, each over all 154 tasks
# split across its 5 VMs. A second pass of the same command fills the rows a run left out (no row is written when the
# MCP server didn't connect, scoring failed or the account hit its limit). Results land in $WAA_HOME/oriel/bench/waa:
# results-oriel-0.1.1-t<run>.jsonl, results-cua-r10-t<run>.jsonl, traces/<tag>/, logs/<tag>-waa<i>.log.
# Usage: round10.sh [runs, default "1 2"]
set -eo pipefail
H=${WAA_HOME:-$HOME/waa}
export WAA_HOME=$H WAA_REMOTE=local CLAUDE_CONFIG_DIR=$H/claude-home RUN_ARGS="--model claude-sonnet-5-5 --effort xhigh"
WARM=$H/WindowsAgentArena/src/win-arena-container/vm/storage-warm
# The Claude Code build installed now serves all runs, even if the machine's claude updates meanwhile.
mkdir -p "$H/bin"
ln -sf "$(readlink -f "$(command -v claude)")" "$H/bin/claude"
export PATH=$H/bin:$PATH
echo "Claude Code $(claude --version)"
cd "$H/oriel/bench/waa"
for t in ${1:-1 2}; do
    echo "run $t: rebuilding the VMs $(date -Is)"
    bash "$H/do-instances.sh" 0 9 "$WARM"
    bash "$H/do-roles.sh" oriel-0.1.1 0 4 &
    bash "$H/do-roles.sh" cua 5 9 &
    wait
    for pass in 1 2; do
        echo "run $t pass $pass: agents start $(date -Is)"
        ./run-do.sh "oriel=oriel-0.1.1-t$t:0-4" "cua=cua-r10-t$t:5-9"
    done
    echo "run $t done $(date -Is): $(wc -l < "results-oriel-0.1.1-t$t.jsonl") oriel rows, $(wc -l < "results-cua-r10-t$t.jsonl") Cua rows"
done
