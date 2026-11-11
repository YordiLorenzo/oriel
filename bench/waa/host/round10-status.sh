#!/bin/bash
# Round 10's state on one screen, from the Mac: ssh firecracker 'bash ~/waa/round10-status.sh'
# The timer, the run's unit (the start check, then round10.sh), rows per results file (153 is a full run), the VMs
# up and the last lines of round10.log. "finished" with result "success" is the end of both runs.
H=${WAA_HOME:-$HOME/waa}
systemctl --user list-timers waa-round10.timer --all --no-pager | head -2
systemctl --user show waa-round10.service -p ActiveState -p Result -p ExecMainStartTimestamp -p ExecMainExitTimestamp \
    -p ExecMainStatus | sed 's/^ExecMainStatus=/exit code=/'
cd "$H/oriel/bench/waa" || exit 1
for f in results-oriel-0.1.3-t1 results-cua-r10-t1 results-oriel-0.1.3-t2 results-cua-r10-t2; do
    echo "$f: $(cat "$f.jsonl" 2>/dev/null | wc -l) of 153 rows"
done
echo "VMs up: $(docker ps --format '{{.Names}}' | grep -E '^waa[0-9]+$' | sort -V | tr '\n' ' ')"
echo "--- $H/round10.log"
tail -n "${1:-8}" "$H/round10.log"
