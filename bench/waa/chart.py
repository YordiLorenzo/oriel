# /// script
# requires-python = ">=3.11"
# dependencies = ["matplotlib==3.9.2"]
# ///
"""The round 9 chart in docs/benchmarks.md, drawn from stats.py's figures.

    uv run --script chart.py [--out ../../docs/images/benchmark-round9.png] [--oriel RUN1 RUN2] [--cua RUN1 RUN2]

Top: oriel's time per task (median), model cost and turns per task as a difference from Cua Driver's, with the 95%
interval from stats.py's bootstrap. Bottom: each layer's completion rate in run 1 and run 2, so the gap between the
layers can be read against the gap between one layer's own two runs. Swapping the --oriel and --cua files must flip
every bar to the other side of zero. It prints the figures it drew, to check against `python3 stats.py`.
"""
import argparse
import math
import os

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
from matplotlib.patches import Polygon  # noqa: E402

import stats  # noqa: E402

# Reference palette, light mode (dataviz skill): surface, ink, chrome, categorical slots 1 and 2.
SURFACE, INK, INK2, MUTED = "#fcfcfb", "#0b0b0b", "#52514e", "#898781"
GRID, BASELINE = "#e1e0d9", "#c3c2b7"
ORIEL, CUA = "#2a78d6", "#eb6834"
DPI = 200
BAR_PX, END_PX = 40, 7  # bar thickness and rounded end in image pixels: about 22 and 4 px at GitHub's page width


def rounded_bar(ax, end, y, color):
    """A bar from 0 to `end`, square at the baseline and rounded at its end, drawn as one shape."""
    fig_w, fig_h = ax.figure.get_size_inches() * DPI
    box = ax.get_position()
    x_px = box.width * fig_w / (ax.get_xlim()[1] - ax.get_xlim()[0])
    y_px = box.height * fig_h / abs(ax.get_ylim()[1] - ax.get_ylim()[0])
    h, side = BAR_PX / y_px, 1 if end > 0 else -1
    rx, ry = min(END_PX / x_px, abs(end) / 2), END_PX / y_px
    arc = [i * math.pi / 16 for i in range(9)]  # a quarter circle
    tip = ([(end - side * rx * (1 - math.sin(a)), y - h / 2 + ry * (1 - math.cos(a))) for a in arc] +
           [(end - side * rx * (1 - math.cos(a)), y + h / 2 - ry * (1 - math.sin(a))) for a in arc])
    ax.add_patch(Polygon([(0, y - h / 2)] + tip + [(0, y + h / 2)], closed=True, facecolor=color, edgecolor="none", zorder=2))
    return h


def style(ax):
    ax.set_facecolor(SURFACE)
    for side in ("top", "right", "left"):
        ax.spines[side].set_visible(False)
    ax.spines["bottom"].set_color(BASELINE)
    ax.tick_params(axis="x", colors=MUTED, labelsize=8.5, length=0, pad=6)
    ax.tick_params(axis="y", length=0, pad=10)
    ax.grid(axis="x", color=GRID, linewidth=0.8)
    ax.set_axisbelow(True)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(here, "..", "..", "docs", "images", "benchmark-round9.png"))
    ap.add_argument("--oriel", nargs=2, default=stats.ORIEL)
    ap.add_argument("--cua", nargs=2, default=stats.CUA)
    args = ap.parse_args()
    out = os.path.abspath(args.out)  # against the caller's folder
    os.chdir(here)  # stats.py reads tasks.json and the results files from here, as when it runs itself
    _, everything, _ = stats.compare(args.oriel, args.cua)
    oriel_runs, cua_runs = stats.run_to_run(args.oriel, args.cua)
    n, o, c = everything["n"], everything["oriel"], everything["cua"]

    plt.rcParams.update({"font.family": ["Arial", "Helvetica", "DejaVu Sans"], "text.color": INK, "text.parse_math": False})
    fig = plt.figure(figsize=(8, 6.1), dpi=DPI, facecolor=SURFACE)
    top = fig.add_axes((0.31, 0.47, 0.64, 0.3))
    bottom = fig.add_axes((0.31, 0.15, 0.64, 0.15))
    fig.text(0.04, 0.955, "WindowsAgentArena round 9: oriel against Cua Driver", fontsize=13.5, fontweight="bold")
    fig.text(0.04, 0.922, f"{n} tasks, two runs per tool layer, the same model and prompt; only the MCP server changes.",
             fontsize=9.5, color=INK2)

    # Top: time, cost and turns as oriel's difference from Cua Driver, with the bootstrap's 95% interval.
    rows = [("median_s", "Time per task, median", "{:.0f} s"), ("cost_run", "Model cost per run", "${:.0f}"),
            ("turns", "Turns per task, mean", "{:.1f}")]
    rel = [everything["rel"][k] for k, _, _ in rows]
    span = max(max(abs(lo), abs(hi)) for _, lo, hi in rel)
    lo_lim = min(0, min(lo for _, lo, _ in rel)) - 0.55 * span
    hi_lim = max(0, max(hi for _, _, hi in rel)) + 0.55 * span
    top.set_xlim(lo_lim, hi_lim)
    top.set_ylim(len(rows) - 0.5, -0.5)
    style(top)
    fig.text(0.04, 0.84, "Time, cost and turns: oriel's difference from Cua Driver", fontsize=11, fontweight="bold")
    fig.text(0.04, 0.812, "Bar: the difference over all tasks. Line: its 95% interval.", fontsize=9, color=INK2)
    for y, ((key, name, unit), (d, lo, hi)) in enumerate(zip(rows, rel)):
        h = rounded_bar(top, d, y, ORIEL)
        top.plot([lo, hi], [y, y], color=INK, linewidth=1.4, solid_capstyle="butt", zorder=3)
        for x in (lo, hi):
            top.plot([x, x], [y - h * 0.32, y + h * 0.32], color=INK, linewidth=1.4, zorder=3)
        side = min(lo, hi) if d < 0 else max(lo, hi)
        word = "lower" if d < 0 else "higher"
        top.text(side + (-1 if d < 0 else 1) * 0.04 * span, y, f"{abs(d):.0f}% {word}", ha="right" if d < 0 else "left",
                 va="center", fontsize=10, fontweight="bold", color=INK)
        top.text(-0.02, y - 0.1, name, transform=top.get_yaxis_transform(), ha="right", va="bottom", fontsize=10)
        top.text(-0.02, y + 0.1, f"oriel {unit.format(o[key])}, Cua Driver {unit.format(c[key])}",
                 transform=top.get_yaxis_transform(), ha="right", va="top", fontsize=9, color=INK2)
    top.axvline(0, color=BASELINE, linewidth=1.2, zorder=1)
    top.set_yticks([])
    top.xaxis.set_major_formatter(lambda v, _: "0" if v == 0 else f"{v:+.0f}%")
    top.set_xlabel("Difference from Cua Driver (left of 0: oriel lower)", fontsize=8.5, color=MUTED, labelpad=6)

    # Bottom: completion rate of each layer's run 1 and run 2, and the mean the summary uses.
    pct = lambda tasks: 100 * tasks / n  # noqa: E731
    layers = [("oriel", ORIEL, oriel_runs), ("Cua Driver", CUA, cua_runs)]
    gap, gap_lo, gap_hi = (pct(v) for v in everything["diff"]["completed"])
    fig.text(0.04, 0.375, "Tasks completed: each layer's two runs", fontsize=11, fontweight="bold")
    fig.text(0.04, 0.347, f"Gap between the layers: {abs(gap):.1f} points (95% interval {min(abs(gap_lo), abs(gap_hi)):.1f} to "
             f"{max(abs(gap_lo), abs(gap_hi)):.1f}). Gap between one layer's own two runs: oriel "
             f"{abs(pct(oriel_runs['diff']['completed'][0])):.1f}, Cua Driver {abs(pct(cua_runs['diff']['completed'][0])):.1f}.",
             fontsize=9, color=INK2)
    values = [pct(r[k]["completed"]) for _, _, r in layers for k in ("cua", "oriel")]  # "cua" is run 1, "oriel" run 2
    bottom.set_xlim(min(values) - 3, max(values) + 3)
    bottom.set_ylim(1.6, -0.6)
    style(bottom)
    for y, (name, color, r) in enumerate(layers):
        run1, run2 = pct(r["cua"]["completed"]), pct(r["oriel"]["completed"])
        mean = (run1 + run2) / 2
        bottom.plot([run1, run2], [y, y], color=color, linewidth=2, solid_capstyle="round", zorder=2)
        bottom.plot([run1, run2], [y, y], "o", markersize=9, color=color, markeredgecolor=SURFACE, markeredgewidth=2, zorder=3)
        bottom.plot([mean, mean], [y - 0.28, y + 0.28], color=INK, linewidth=1.2, zorder=4)
        bottom.text(mean, y - 0.36, f"mean {mean:.0f}%", ha="center", va="bottom", fontsize=8.5, color=INK2)
        bottom.text(-0.02, y - 0.06, name, transform=bottom.get_yaxis_transform(), ha="right", va="bottom", fontsize=10)
        bottom.text(-0.02, y + 0.06, f"run 1 {run1:.1f}%, run 2 {run2:.1f}%", transform=bottom.get_yaxis_transform(),
                    ha="right", va="top", fontsize=9, color=INK2)
    bottom.set_yticks([])
    bottom.xaxis.set_major_formatter(lambda v, _: f"{v:.0f}%")
    bottom.set_xlabel(f"Tasks completed, % of {n}", fontsize=8.5, color=MUTED, labelpad=6)

    fig.text(0.04, 0.035, "Claude Sonnet 5.5 in Claude Code, 50-turn limit. oriel v12; Cua Driver 0.38.0 without 4 of its 63 tools "
             "(run_script, 2 that install software, check_for_update).\nCost is Claude Code's list-price estimate. Intervals: bootstrap over "
             "tasks, 10,000 draws, bench/waa/stats.py. Rows: bench/waa/results-*.jsonl.", fontsize=7.5, color=MUTED,
             linespacing=1.5)
    fig.savefig(out, dpi=DPI, facecolor=SURFACE, metadata={"Software": None})

    for (key, name, _), (d, lo, hi) in zip(rows, rel):
        print(f"{name}: {d:.0f}% ({lo:.0f}% to {hi:.0f}%)")
    print(f"completion gap: {gap:.1f} points ({gap_lo:.1f} to {gap_hi:.1f})")
    for name, _, r in layers:
        print(f"{name}: run 1 {pct(r['cua']['completed']):.1f}%, run 2 {pct(r['oriel']['completed']):.1f}%")
    print(f"wrote {out}")


if __name__ == "__main__":
    main()
