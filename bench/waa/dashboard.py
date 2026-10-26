#!/usr/bin/env python3
"""Local live dashboard for the WAA runs: one row per task, one column per tool layer.

    python3 dashboard.py [--watch]      then open dashboard.html

Reads tasks.json (the 154 tasks), every results-<layer>.jsonl here, and the newest trace per layer (the task running
now). Writes dashboard-data.js, which dashboard.html reloads every 5 s; --watch rewrites it every 5 s.
"""
import glob
import json
import os
import statistics
import subprocess
import sys
import time

# Columns in this order: round 6 (oriel v8 and Cua, two runs each) first, then round 5; other results-*.jsonl
# files (earlier rounds) are added after these.
LAYERS = ["oriel-v12-t1", "cua-r9-t1", "oriel-v12-t2", "cua-r9-t2", "oriel-v11-t1", "cua-r8-t1", "oriel-v11-t2", "cua-r8-t2"]


def tool_calls(trace: str) -> list:
    calls = []
    try:
        for line in open(trace):
            if '"tool_use"' not in line:
                continue
            for c in json.loads(line).get("message", {}).get("content", []):
                if c.get("type") == "tool_use":
                    calls.append(c["name"].split("__")[-1] + " " + json.dumps(c["input"], ensure_ascii=False)[:120])
    except (OSError, ValueError):
        pass
    return calls


def layer_state(layer: str, tasks: list) -> dict:
    rows = {}
    try:
        for line in open(f"results-{layer}.jsonl"):
            r = json.loads(line)
            rows[r["task"]] = {k: r.get(k) for k in ("score", "turns", "wall_s", "cost_usd", "subtype", "infeasible_claimed")}
            rows[r["task"]]["reply"] = (r.get("result") or "")[-700:]
            rows[r["task"]]["calls"] = len(tool_calls(f"traces/{layer}/{r['task']}.jsonl"))
    except FileNotFoundError:
        pass
    # Tasks with a trace but no row yet are running; on the droplet one layer runs on many VMs at once.
    alive = subprocess.run(["pgrep", "-f", f"run.py --layer .*(--tag {layer}|results-{layer}.jsonl)"],
                           capture_output=True).returncode == 0 or \
        subprocess.run(["pgrep", "-f", f"run.py --layer {layer} "], capture_output=True).returncode == 0
    running = []
    if alive:
        for t in glob.glob(f"traces/{layer}/*.jsonl"):
            task = os.path.basename(t)[:-6]
            if task in rows or time.time() - os.path.getmtime(t) > 1200:
                continue
            calls = tool_calls(t)
            # st_birthtime is when the agent started (macOS); getctime is the last metadata change, reset by every write.
            running.append({"task": task, "since_s": round(time.time() - os.stat(t).st_birthtime),
                            "calls": len(calls), "last": calls[-3:]})
    done = list(rows.values())
    split = {t["id"]: t.get("split") for t in tasks}
    halves = {h: [r for t, r in rows.items() if split.get(t) == h] for h in ("dev", "holdout")}
    walls = [r["wall_s"] for r in done if r["wall_s"]]
    remaining = len(tasks) - len(done)
    return {
        "rows": rows, "running": running, "alive": alive, "done": len(done),
        "halves": {h: [round(sum(r["score"] or 0 for r in rs), 1), len(rs)] for h, rs in halves.items()},
        "score": round(sum(r["score"] or 0 for r in done), 2),
        "cost": round(sum(r["cost_usd"] or 0 for r in done), 2),
        "median_s": round(statistics.median(walls)) if walls else None,
        "eta_min": round(remaining * (statistics.mean(walls) + 25) / 60 / max(1, len(running))) if walls and alive else None,
    }


def build():
    tasks = json.load(open("tasks.json"))
    layers = LAYERS + sorted({os.path.basename(f)[8:-6] for f in glob.glob("results-*.jsonl")} - set(LAYERS))
    data = {"updated": time.strftime("%H:%M:%S"), "tasks": tasks, "layers": layers,
            "state": {l: layer_state(l, tasks) for l in layers}}
    tmp = f"dashboard-data.js.{os.getpid()}.tmp"  # per process, so two refreshers can't trip over one temp file
    with open(tmp, "w") as f:
        f.write("window.DASH = " + json.dumps(data, ensure_ascii=False) + ";\n")
    os.replace(tmp, "dashboard-data.js")


if __name__ == "__main__":
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    build()
    while "--watch" in sys.argv:
        time.sleep(5)
        build()
