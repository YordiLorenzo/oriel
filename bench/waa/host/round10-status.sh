#!/bin/bash
# Round 10's state on one screen, from the Mac: ssh firecracker 'bash ~/waa/round10-status.sh'
# One state line (waiting for the timer, start check running, run running, finished or not started, with the time),
# rows per results file (153 is a full run), the VMs up and the last lines of round10.log.
H=${WAA_HOME:-$HOME/waa}
eval "$(systemctl --user show waa-round10.service -p ActiveState -p ExecMainStartTimestamp -p ExecMainExitTimestamp \
    -p ExecMainStatus | sed 's/=\(.*\)/="\1"/')"
if [ -z "$ExecMainStartTimestamp" ]; then
    echo "state: waiting for the timer, next start $(systemctl --user show waa-round10.timer -p NextElapseUSecRealtime --value)"
elif [ "$ActiveState" = active ]; then
    grep -q 'gate: all clear' "$H/round10.log" && what="the run is running" || what="the start check is running"
    echo "state: $what (since $ExecMainStartTimestamp)"
elif [ "$ExecMainStatus" = 0 ]; then
    echo "state: finished, both runs done ($ExecMainExitTimestamp)"
else
    echo "state: stopped with exit code $ExecMainStatus ($ExecMainExitTimestamp); the log's last lines say why"
fi
cd "$H/oriel/bench/waa" || exit 1
for f in results-oriel-0.1.3-t1 results-cua-r10-t1 results-oriel-0.1.3-t2 results-cua-r10-t2; do
    echo "$f: $(cat "$f.jsonl" 2>/dev/null | wc -l) of 153 rows"
done
echo "VMs up: $(docker ps --format '{{.Names}}' | grep -E '^waa[0-9]+$' | sort -V | tr '\n' ' ')"
echo "--- $H/round10.log"
tail -n "${1:-8}" "$H/round10.log" 2>/dev/null || echo "(no log yet)"
