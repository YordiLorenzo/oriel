#!/bin/bash
# Run tool layers on the droplet's instances in parallel, each layer's tasks split across its containers.
# Usage: WAA_DO_HOST=<ip> ./run-do.sh <spec> [<spec> ...]
#   spec = <layer>[=<tag>]:<first>-<last>[@<offset>/<total>]
#   oriel:0-6 cua:7-13              oriel on waa0..waa6 and Cua on waa7..waa13, each split 7 ways
#   oriel=oriel-v2:7-13@0/14        tag oriel-v2 on waa7..waa13 as shards 0..6 of 14 (the other 7 can start later)
cd "$(dirname "$0")"
mkdir -p logs
for spec in "$@"; do
    head=${spec%%:*}; rest=${spec#*:}
    layer=${head%%=*}; tag=${head#*=}
    range=${rest%%@*}; first=${range%-*}; last=${range#*-}
    if [ "$rest" != "$range" ]; then split=${rest#*@}; offset=${split%/*}; total=${split#*/}
    else offset=0; total=$((last - first + 1)); fi
    for i in $(seq "$first" "$last"); do
        python3 run.py --layer "$layer" --tag "$tag" --remote do --container "waa$i" --shard "$((offset + i - first))/$total" \
            --out "results-$tag.jsonl" > "logs/$tag-waa$i.log" 2>&1 &
    done
done
wait
