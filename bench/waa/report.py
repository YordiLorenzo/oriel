#!/usr/bin/env python3
"""Write RESULTS.md from the results-*.jsonl files: each round's layers compared on the tasks they all finished,
per app and per half (dev = traces read while fixing oriel, held-out = never read).

    python3 report.py
"""
import json
import os
import statistics
from collections import defaultdict

ROUNDS = [
    ("Round 3: warmed image, oriel v4", ["oriel-v4", "cua-warm", "oriel-shell-v4"],
     "LibreOffice started once before saving the image (its first-run updater done), the Chrome/Edge profile patch, "
     "WAA's server hidden. oriel v4: foregrounding fixed, click and drag, keys fallback for hung apps."),
    ("Round 1: cold image, oriel v1", ["oriel", "cua"],
     "Every fresh VM ran LibreOffice's updater on first launch (minutes, sometimes looping); the oriel run was "
     "stopped at 116 tasks."),
    ("oriel v2 on the cold image", ["oriel-v2", "oriel"],
     "Fixes before the foreground bug was found; stopped at 101 tasks."),
]
NAMES = {"oriel-v4": "oriel v4", "cua-warm": "Cua Driver 0.38.0", "oriel-shell-v4": "oriel v4 + shell",
         "oriel": "oriel v1", "cua": "Cua Driver 0.38.0", "oriel-v2": "oriel v2"}


def load(layer):
    path = f"results-{layer}.jsonl"
    if not os.path.exists(path):
        return {}
    return {r["task"]: r for r in map(json.loads, open(path))}


def two_runs(tasks, oriel, cua, title, note):
    """A round of oriel and Cua, two runs each; tasks scored as the mean of their two runs."""
    runs = {l: load(l) for l in (oriel + "-t1", oriel + "-t2", cua + "-t1", cua + "-t2")}
    if not all(runs.values()):
        return []
    shared = sorted(set.intersection(*(set(d) for d in runs.values())))
    mean = lambda layer, k: (runs[layer + "-t1"][k]["score"] + runs[layer + "-t2"][k]["score"]) / 2
    o_, c_ = oriel, cua
    run = lambda name, ks: sum(runs[name][k]["score"] for k in ks)
    out = [f"## {title}", "", note + " Scores are the mean of the two runs.", "",
           f"{len(shared)} tasks finished in all four runs.", "",
           f"| | oriel {oriel.split('-')[1]} | Cua Driver 0.38.0 | oriel run 1 / run 2 | Cua run 1 / run 2 |", "|---|---|---|---|---|"]
    halves = (("All", shared), ("Dev half", [k for k in shared if tasks[k]["split"] == "dev"]),
              ("Held-out half", [k for k in shared if tasks[k]["split"] == "holdout"]))
    for label, ks in halves:
        o, c = sum(mean(o_, k) for k in ks), sum(mean(c_, k) for k in ks)
        out.append(f"| **{label}** ({len(ks)}) | {o:.1f} ({100 * o / max(1, len(ks)):.0f}%) | {c:.1f} ({100 * c / max(1, len(ks)):.0f}%) | "
                   f"{run(o_ + '-t1', ks):.0f} / {run(o_ + '-t2', ks):.0f} | {run(c_ + '-t1', ks):.0f} / {run(c_ + '-t2', ks):.0f} |")
    by_app = defaultdict(list)
    for k in shared:
        by_app[tasks[k]["domain"]].append(k)
    for app, ks in sorted(by_app.items()):
        out.append(f"| {app} ({len(ks)}) | {sum(mean(o_, k) for k in ks):.1f} | {sum(mean(c_, k) for k in ks):.1f} | "
                   f"{run(o_ + '-t1', ks):.0f} / {run(o_ + '-t2', ks):.0f} | {run(c_ + '-t1', ks):.0f} / {run(c_ + '-t2', ks):.0f} |")
    cost = lambda layer: sum((runs[layer + t][k]["cost_usd"] or 0) for t in ("-t1", "-t2") for k in shared) / 2
    out.append(f"| cost per run | ${cost(o_):.0f} | ${cost(c_):.0f} | | |")
    per = lambda layer, key, f: f([runs[layer + t][k][key] or 0 for t in ("-t1", "-t2") for k in shared])
    out.append(f"| time per task, median / mean | {per(o_, 'wall_s', statistics.median):.0f} s / {per(o_, 'wall_s', statistics.mean):.0f} s | "
               f"{per(c_, 'wall_s', statistics.median):.0f} s / {per(c_, 'wall_s', statistics.mean):.0f} s | | |")
    out.append(f"| turns per task | {per(o_, 'turns', statistics.mean):.1f} | {per(c_, 'turns', statistics.mean):.1f} | | |")
    agree = lambda layer: sum(runs[layer + "-t1"][k]["score"] == runs[layer + "-t2"][k]["score"] for k in shared)
    out += ["", f"Run-to-run agreement: oriel gave the same result on {agree(o_)} of {len(shared)} tasks, Cua on "
                f"{agree(c_)}; the rest flipped between runs, which is the noise a single run carries.", ""]
    return out


def main():
    tasks = {t["id"]: t for t in json.load(open("tasks.json"))}
    out = ["# WindowsAgentArena results", "",
           "Claude Sonnet 5.5 through Claude Code, the same prompt and a 50-turn limit for every layer; only the MCP "
           "server changes. WAA's own setup and evaluators score each task (see README.md for the harness and the "
           "rules against tuning to the benchmark). Rounds 1-3 ran each task once, so differences of a few tasks "
           "there are noise; round 4 ran each task twice.", ""]
    out += two_runs(tasks, "oriel-v12", "cua-r9", "Round 9: oriel v12 vs Cua, two runs each, fresh VMs (current)",
                    "As round 8. oriel v12: keys types a parenthesis or bracket that no modifier opens as itself "
                    "(=SUM(A1:A3) used to be refused until escaped), and a window that matches nothing lists the open "
                    "windows in the error. Tasks cut short by the account's usage limit or by the Mac losing its "
                    "connection to the VMs were rerun; the Chrome task whose evaluator returns HTTP 500 is left out.")
    out += two_runs(tasks, "oriel-v11", "cua-r8", "Round 8: oriel v11 vs Cua, two runs each, fresh VMs",
                    "As round 7, with every VM rebuilt from the warmed image before run 1 and a leftover Windows Firewall "
                    "prompt closed (Cancel) in the clean slate. oriel v10 and v11: the tools load up front (one model turn "
                    "fewer), a first lookup in a window reuses the change report's walk, keys checks the focus in one call, "
                    "and a notification toast holding the foreground no longer blocks keys. Run 2 was rerun from the start "
                    "after the Mac running the agents lost its connection; a task whose scoring failed in both runs (a "
                    "Chrome evaluator returning HTTP 500) is left out.")
    out += two_runs(tasks, "oriel-v9", "cua-r7", "Round 7: oriel v9 vs Cua, two runs each, 10 VMs",
                    "As round 6 on 10 VMs instead of 14, so each VM has more CPU (14 kept the droplet at load 32-48 on "
                    "32 vCPUs). oriel v9: invoke, set, keys, click, drag and batch can return a picture of the window with "
                    "their reply (screenshot=true), saving the separate screenshot calls agents made 2.7 times a task.")
    out += two_runs(tasks, "oriel-v8", "cua-r6", "Round 6: oriel v8 vs Cua, two runs each",
                    "As round 5, with Windows Update, its orchestrator, search indexing and SysMain off in every VM from "
                    "the start. oriel v8: the fallbacks for hung apps also catch the TimeoutException form of UIA "
                    "timeouts (they had been skipped), Tab/Enter then text allowed in spreadsheet cells.")
    out += two_runs(tasks, "oriel-v7", "cua-r5", "Round 5: oriel v7 vs Cua, two runs each",
                    "As round 4, and the clean slate before each task also clears LibreOffice's recovery list (killing it "
                    "had made the next start open \"Document Recovery\" in a fifth of round 4's tasks). oriel v7: wait "
                    "answers once a window has gone quiet, faster typing, server instructions that cover keys, click and drag.")
    out += two_runs(tasks, "oriel-v6", "cua-r4", "Round 4: oriel v6 vs Cua, two runs each",
                    "The warmed image as in round 3, plus a clean slate before every task (LibreOffice and \"Pick an app\" "
                    "dialogs closed, as WAA's own setup gets by reverting the VM). oriel v6: keys through paced SendInput "
                    "(modifiers no longer dropped) and keys to hung apps.")
    for title, layers, note in ROUNDS:
        data = {l: load(l) for l in layers}
        if not all(data.values()):
            continue
        shared = sorted(set.intersection(*(set(d) for d in data.values())))
        out += [f"## {title}", "", note, "", f"{len(shared)} tasks finished by every layer.", ""]
        out.append("| | " + " | ".join(NAMES[l] for l in layers) + " |")
        out.append("|---|" + "---|" * len(layers))
        score = lambda l, ks: sum(data[l][k]["score"] for k in ks)
        for label, ks in (("All", shared), ("Dev half", [k for k in shared if tasks[k]["split"] == "dev"]),
                          ("Held-out half", [k for k in shared if tasks[k]["split"] == "holdout"])):
            out.append(f"| **{label}** ({len(ks)}) | " + " | ".join(f"{score(l, ks):.0f} ({100 * score(l, ks) / max(1, len(ks)):.0f}%)" for l in layers) + " |")
        by_app = defaultdict(list)
        for k in shared:
            by_app[tasks[k]["domain"]].append(k)
        for app, ks in sorted(by_app.items()):
            out.append(f"| {app} ({len(ks)}) | " + " | ".join(f"{score(l, ks):.0f}" for l in layers) + " |")
        out.append("| median turns | " + " | ".join(f"{statistics.median(data[l][k]['turns'] or 50 for k in shared):.0f}" for l in layers) + " |")
        out.append("| median time per task | " + " | ".join(f"{statistics.median(data[l][k]['wall_s'] for k in shared):.0f} s" for l in layers) + " |")
        out.append("| cost | " + " | ".join(f"${sum(data[l][k]['cost_usd'] or 0 for k in shared):.0f}" for l in layers) + " |")
        out.append("")
    open("RESULTS.md", "w").write("\n".join(out) + "\n")
    print("\n".join(out))


if __name__ == "__main__":
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    main()
