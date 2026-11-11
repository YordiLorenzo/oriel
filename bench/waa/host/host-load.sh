#!/bin/bash
# One JSON line for run.py's rows: the WAA host's 1-minute load average and, where the PSU reports it (firecracker's
# Corsair HX1500i through the corsair-psu hwmon), its total DC output in watts; null where there is none.
read -r load _ < /proc/loadavg
watts=null
for h in /sys/class/hwmon/hwmon*; do
    [ "$(cat "$h/name")" = corsairpsu ] && watts=$(($(cat "$h/power1_input") / 1000000))
done
echo "{\"load1\": $load, \"psu_w\": $watts}"
