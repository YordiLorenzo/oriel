#!/usr/bin/env python3
"""Bootstrap 95% confidence intervals for round 9: oriel minus Cua Driver in completion, time, cost and turns.

    python3 stats.py [--seed 1] [--reps 10000] [--oriel RUN1 RUN2] [--cua RUN1 RUN2]

Only tasks scored in all four runs are compared. A task's completion is the mean of its two runs, as in RESULTS.md.
Each resample draws that many tasks with replacement and keeps both runs of both layers for each task drawn, so the
pairing by task stays. The interval is the 2.5th to 97.5th percentile of the resampled differences. The feasible
columns leave out the tasks that tasks.json marks infeasible. The third table compares each layer's run 2 with its run 1
in the same way. RESULTS.md shows the second and third tables this prints.
"""
import argparse
import json
import os
import random
import statistics

ORIEL = ["results-oriel-v12-t1.jsonl", "results-oriel-v12-t2.jsonl"]
CUA = ["results-cua-r9-t1.jsonl", "results-cua-r9-t2.jsonl"]
KEYS = ["completed", "median_s", "mean_s", "cost_run", "turns"]


def load(path):
    return {r["task"]: r for r in map(json.loads, open(path))}


def measures(tasks, runs):
    """One layer's figures over `tasks` (a task may appear more than once in a resample)."""
    rows = [run[t] for t in tasks for run in runs]
    return {
        "completed": sum(sum(run[t]["score"] for run in runs) / len(runs) for t in tasks),
        "median_s": statistics.median(r["wall_s"] for r in rows),
        "mean_s": statistics.mean(r["wall_s"] for r in rows),
        "cost_run": sum(r["cost_usd"] or 0 for r in rows) / len(runs),
        "turns": statistics.mean(r["turns"] or 0 for r in rows),
    }


def ci(values):
    values = sorted(values)
    return values[int(0.025 * len(values))], values[int(0.975 * len(values)) - 1]


def bootstrap(tasks, oriel, cua, reps, seed):
    """Each layer's figures, and for each figure the difference with its 95% interval, absolute and relative."""
    o, c = measures(tasks, oriel), measures(tasks, cua)
    rng = random.Random(seed)
    diffs = {k: [] for k in KEYS}
    rel = {k: [] for k in KEYS}
    for _ in range(reps):
        sample = rng.choices(tasks, k=len(tasks))
        so, sc = measures(sample, oriel), measures(sample, cua)
        for k in KEYS:
            diffs[k].append(so[k] - sc[k])
            rel[k].append(100 * (so[k] - sc[k]) / sc[k])
    return {"n": len(tasks), "oriel": o, "cua": c,
            "diff": {k: (o[k] - c[k], *ci(diffs[k])) for k in KEYS},
            "rel": {k: (100 * (o[k] - c[k]) / c[k], *ci(rel[k])) for k in KEYS}}


def compare(oriel_paths=ORIEL, cua_paths=CUA, reps=10000, seed=1):
    info = {t["id"]: t for t in json.load(open("tasks.json"))}
    oriel, cua = [load(p) for p in oriel_paths], [load(p) for p in cua_paths]
    shared = sorted(set.intersection(*(set(run) for run in oriel + cua)))
    feasible = [t for t in shared if not info[t]["infeasible"]]
    return len(info), bootstrap(shared, oriel, cua, reps, seed), bootstrap(feasible, oriel, cua, reps, seed)


def run_to_run(oriel_paths=ORIEL, cua_paths=CUA, reps=10000, seed=1):
    """Run 2 minus run 1 of each layer on the same tasks: what this method finds between two runs of one set-up."""
    oriel, cua = [load(p) for p in oriel_paths], [load(p) for p in cua_paths]
    shared = sorted(set.intersection(*(set(run) for run in oriel + cua)))
    return [bootstrap(shared, runs[1:], runs[:1], reps, seed) for runs in (oriel, cua)]


def run_to_run_table(o, c):
    names = {"completed": "completion rate", "median_s": "time per task, median", "mean_s": "time per task, mean",
             "cost_run": "model cost", "turns": "turns per task, mean"}
    out = [f"| run 2 minus run 1, {o['n']} tasks | oriel | 95% interval | Cua Driver | 95% interval |", "|---|---|---|---|---|"]
    for k, name in names.items():
        cells = []
        for r in (o, c):
            if k == "completed":
                d, lo, hi = r["diff"][k]
                cells += [f"{100 * d / r['n']:.1f} points", f"{100 * lo / r['n']:.1f} to {100 * hi / r['n']:.1f}"]
            else:
                p, plo, phi = r["rel"][k]
                cells += [f"{p:.0f}%", f"{plo:.0f}% to {phi:.0f}%"]
        out.append(f"| {name} | " + " | ".join(cells) + " |")
    return out


def levels_table(everything, feasible):
    out = ["| | oriel, all | Cua Driver, all | oriel, feasible | Cua Driver, feasible |", "|---|---|---|---|---|"]
    names = {"completed": ("tasks completed (mean of 2 runs)", "{:.1f}"), "median_s": ("time per task, median (s)", "{:.0f}"),
             "mean_s": ("time per task, mean (s)", "{:.0f}"), "cost_run": ("model cost per run ($)", "{:.2f}"),
             "turns": ("turns per task, mean", "{:.1f}")}
    for k, (name, f) in names.items():
        out.append(f"| {name} | {f.format(everything['oriel'][k])} | {f.format(everything['cua'][k])} | "
                   f"{f.format(feasible['oriel'][k])} | {f.format(feasible['cua'][k])} |")
    return out


def difference_table(everything, feasible):
    """The table in RESULTS.md: oriel minus Cua Driver with 95% intervals, for all tasks and for feasible ones."""
    def cells(r, k):
        d, lo, hi = r["diff"][k]
        p, plo, phi = r["rel"][k]
        n = r["n"]
        if k == "completed":
            return [(f"{d:.1f}", f"{lo:.1f} to {hi:.1f}"), (f"{100 * d / n:.1f} points", f"{100 * lo / n:.1f} to {100 * hi / n:.1f}")]
        unit = {"median_s": "{:.0f} s", "mean_s": "{:.0f} s", "cost_run": "${:.2f}", "turns": "{:.1f}"}[k]
        value = ("-" if d < 0 else "") + unit.format(abs(d))
        return [(f"{value} ({p:.0f}%)", f"{plo:.0f}% to {phi:.0f}%")]
    names = {"completed": ["tasks completed (mean of 2 runs)", "completion rate"], "median_s": ["time per task, median"],
             "mean_s": ["time per task, mean"], "cost_run": ["model cost per run"], "turns": ["turns per task, mean"]}
    out = [f"| oriel minus Cua Driver | All {everything['n']} tasks | 95% interval | {feasible['n']} feasible tasks | 95% interval |",
           "|---|---|---|---|---|"]
    for k in KEYS:
        for name, (a, ai), (f, fi) in zip(names[k], cells(everything, k), cells(feasible, k)):
            out.append(f"| {name} | {a} | {ai} | {f} | {fi} |")
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--reps", type=int, default=10000)
    ap.add_argument("--oriel", nargs=2, default=ORIEL)
    ap.add_argument("--cua", nargs=2, default=CUA)
    args = ap.parse_args()
    total, everything, feasible = compare(args.oriel, args.cua, args.reps, args.seed)
    print(f"oriel: {' + '.join(args.oriel)}")
    print(f"Cua Driver: {' + '.join(args.cua)}")
    print(f"{everything['n']} of {total} tasks compared (scored in all four runs), {feasible['n']} of them feasible. "
          f"Bootstrap over tasks, {args.reps} resamples, seed {args.seed}.\n")
    print("\n".join(levels_table(everything, feasible)) + "\n")
    print("\n".join(difference_table(everything, feasible)) + "\n")
    print("\n".join(run_to_run_table(*run_to_run(args.oriel, args.cua, args.reps, args.seed))))


if __name__ == "__main__":
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    main()
