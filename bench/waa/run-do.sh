#!/bin/bash
# Run tool layers on the WAA host's instances in parallel, each layer's tasks split across its containers.
# Usage: WAA_DO_HOST=<ip> ./run-do.sh <spec> [<spec> ...]          from a client, with a droplet as the host
#        WAA_REMOTE=local WAA_HOME=<dir> ./run-do.sh <spec> [...]   on the host itself (firecracker)
#   spec = <layer>[=<tag>]:<first>-<last>[@<offset>/<total>]
#   oriel:0-6 cua:7-13              oriel on waa0..waa6 and Cua on waa7..waa13, each split 7 ways
#   oriel=oriel-v2:7-13@0/14        tag oriel-v2 on waa7..waa13 as shards 0..6 of 14 (the other 7 can start later)
# More run.py options for every copy go in $RUN_ARGS, e.g. RUN_ARGS="--model claude-sonnet-5-5 --effort xhigh".
cd "$(dirname "$0")"
mkdir -p logs
for spec in "$@"; do
    head=${spec%%:*}; rest=${spec#*:}
    layer=${head%%=*}; tag=${head#*=}
    range=${rest%%@*}; first=${range%-*}; last=${range#*-}
    if [ "$rest" != "$range" ]; then split=${rest#*@}; offset=${split%/*}; total=${split#*/}
    else offset=0; total=$((last - first + 1)); fi
    for i in $(seq "$first" "$last"); do
        python3 run.py --layer "$layer" --tag "$tag" --remote "${WAA_REMOTE:-do}" --container "waa$i" \
            --shard "$((offset + i - first))/$total" --out "results-$tag.jsonl" $RUN_ARGS > "logs/$tag-waa$i.log" 2>&1 &
    done
done
wait
