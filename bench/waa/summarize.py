#!/usr/bin/env python3
"""Summarize a run.py output file: success rate per WAA domain, turns, time, cost.

    python3 summarize.py results-oriel.jsonl [more.jsonl ...]

Turns, cost and the agent's final reply come from the saved trace (traces/<layer>/<task>.jsonl) when the row lacks
them: rows from before the stream-json parsing fix (the first 12 of the oriel run) recorded none. Infeasible tasks
are rescored the way WAA scores them, from the agent's INFEASIBLE claim, since those rows also missed the claim.
"""
import json
import statistics
import subprocess
import sys
from collections import defaultdict

from run import REMOTES  # the WAA host: a Windows PC with the WSL distro "waa", its ssh name in $ORIEL_BENCH_SSH

TASKS = "/root/WindowsAgentArena/src/win-arena-container/client/evaluation_examples_windows/examples"


def evaluator_funcs() -> dict:
    script = f"""
import glob, json, os
out = {{}}
for f in glob.glob('{TASKS}/*/*.json'):
    out[os.path.basename(f)[:-5]] = json.load(open(f))['evaluator']['func']
print(json.dumps(out))
"""
    p = subprocess.run([*REMOTES["wsl"], "python3", "-"], input=script, capture_output=True, text=True, check=True)
    return json.loads(p.stdout)


def final_event(layer: str, task: str) -> dict:
    try:
        lines = open(f"traces/{layer}/{task}.jsonl").read().splitlines()
    except FileNotFoundError:
        return {}
    events = [json.loads(line) for line in lines if line.startswith("{")]
    return next((e for e in reversed(events) if e.get("type") == "result"), {})


def main():
    funcs = evaluator_funcs()
    for path in sys.argv[1:]:
        rows = [json.loads(line) for line in open(path)]
        by_domain = defaultdict(list)
        for r in rows:
            if r.get("turns") is None:
                e = final_event(r["layer"], r["task"])
                r["turns"], r["cost_usd"], r["result"] = e.get("num_turns"), e.get("total_cost_usd"), e.get("result", "")
            claimed = (r.get("result") or "").rstrip().rstrip(".").endswith("INFEASIBLE")
            if funcs.get(r["task"]) == "infeasible":
                r["score"] = 1.0 if claimed else 0.0
            elif claimed:
                r["score"] = 0.0
            by_domain[r["domain"]].append(r)
        total = sum(r["score"] for r in rows)
        print(f"\n{path}: {total:.1f}/{len(rows)} = {100 * total / len(rows):.1f}%")
        print(f"{'domain':18} {'score':>10} {'median turns':>13} {'median s':>9} {'cost':>7}")
        for domain, rs in sorted(by_domain.items()):
            s = sum(r["score"] for r in rs)
            turns = [r["turns"] for r in rs if r["turns"] is not None]
            print(f"{domain:18} {s:5.1f}/{len(rs):<4} {statistics.median(turns) if turns else 0:>13} "
                  f"{statistics.median(r['wall_s'] for r in rs):>9.0f} {sum(r['cost_usd'] or 0 for r in rs):>7.2f}")
        print(f"{'all':18} {total:5.1f}/{len(rows):<4} {'':>13} {'':>9} {sum(r['cost_usd'] or 0 for r in rows):>7.2f}")


if __name__ == "__main__":
    main()
