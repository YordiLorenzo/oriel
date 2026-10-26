# Benchmarks

Sections 1, 2 and 4 come from one Windows 11 Pro PC (build 26200, Ryzen 5 7600X, 32 GB), driven from a MacBook over a gigabit LAN (about 3 ms round trip), on 2026-10-21/22. That PC was **locked** throughout, which rules out keyboard input and suspends Store apps (Calculator, Settings), so those aren't measured there. Section 3 ran in WindowsAgentArena's own Windows 11 VMs, unlocked.

The scripts, and how to run them on your own PC, are in [bench/README.md](../bench/README.md).

## Summary

| Benchmark | What it measures | Result |
|---|---|---|
| [3. WindowsAgentArena](#3-windowsagentarena-benchwaa) | Whole tasks: Microsoft's 153 scored tasks in a Windows 11 VM, the same model on both sides, only the MCP server swapped | With oriel, 30% less time per task (median), 38% lower model cost, 19% fewer model turns. Cua Driver completed 4 points more tasks (67% against 63%), close to the run-to-run noise. |
| [2. Head-to-head](#2-head-to-head-benchheadtohead) | Single calls through four Windows MCP servers on the same PC | oriel was fastest on every call measured, for example listing windows in 6 ms (Cua Driver 501 ms) and launching an app in 203 ms (Cua Driver 902 ms). |
| [1. Latency per call](#1-latency-per-call-benchlatencypy) | oriel's own time per call, over SSH | Most calls take 5 to 60 ms. |
| [4. Agent task suite](#4-agent-task-suite-benchtasks) | 18 tasks written alongside oriel | oriel passed 36 of 36 runs, Cua Driver 16 of 18. WindowsAgentArena is the larger, external test. |

## 1. Latency per call (`bench/latency.py`)

oriel's daemon, reached over SSH the way `ssh pc oriel mcp` reaches it. The table gives the median wall time on the Mac; the network share is about 5 ms.

| Step | Time |
|---|---|
| ping (the floor: SSH and network) | 5–8 ms |
| list windows | 6 ms |
| press a button, by name or ref | 4–6 ms |
| fill a field, by partial name | 16–18 ms |
| read a field | 16 ms |
| press + change report | 40–55 ms |
| tree: Character Map / Chrome / Edge | 22–28 / 28–30 / 38–40 ms |
| tree: File Explorer (settled / just opened) | 52–56 / 190–215 ms |
| launch Character Map (+ its tree, one call) | 190–200 (266–300) ms |
| a typo, answered with the closest names | 840 ms (0.8 s of retries while controls may still be appearing) |

## 2. Head-to-head (`bench/headtohead/`)

The same tasks run through four MCP servers on the same PC. Each server ran in the desktop session, and each client ran on the Mac with the fastest settings that tool offers (no screenshots, highlighting or post-action diffs). 5 runs per task (3 for Windows-MCP); medians:

| Task | oriel | Cua Driver 0.38.0 | Terminator 0.24.28 | Windows-MCP 0.8.8 |
|---|---|---|---|---|
| List windows | **6 ms** | 501 ms | 549 ms | in Snapshot |
| Launch Character Map | **203 ms** | 902 ms | 2,384 ms | 1,182 ms |
| Character Map tree | **23 ms** | 42 ms (+ ~500 ms list_windows) | 2,603 ms | 14,034 ms (whole desktop) |
| Chrome window tree | **28 ms** | 48 ms | 2,684 ms | (snapshot) |
| Fill a field (ref / token / selector) | **8 ms** | 11 ms | 4,698 ms | not run* |
| Press a button | **8 ms** | 13 ms | 2,813 ms | not run* |
| Act + report what changed | **45 ms**, 1 call | 2 calls | option, not timed | 2 calls |
| Correct outcome | 15/15 | 5/5 | 2/2 | n/a |

\* Windows-MCP's click and type send real mouse and keyboard input, which on a locked PC goes to the lock screen.

**Reading it:**
- **Cua Driver** is the only close competitor: within a few milliseconds per action, about 1.7× slower on window trees, and much slower to list windows (which it needs in order to find one) and to launch.
- **Terminator** takes seconds per call, even with its default per-call screenshot turned off.
- **Windows-MCP** sees the screen through a whole-desktop snapshot, which takes 14 s.

## 3. WindowsAgentArena (`bench/waa/`)

[WindowsAgentArena](https://github.com/microsoft/WindowsAgentArena) is Microsoft's 154-task benchmark in a real Windows 11 VM:
- Chrome, Edge, File Explorer;
- LibreOffice Calc and Writer, VS Code, VLC;
- Settings, Paint, Clock, Calculator, Notepad.

The harness keeps WAA's own setup and evaluators for every task and swaps only the agent's tools:
- **Agent:** Claude Sonnet 5.5 through Claude Code, the same prompt and turn limit for every layer.
- **Hosting:** one DigitalOcean droplet, 10 VMs from round 7 on (14 before).
- **Patches:** WAA no longer runs as published, so `host/waa-patch.py` repairs its setup (dead LibreOffice mirror, hanging Caddy download, missing VC++ runtime, Chrome 136+ ignoring remote debugging on the default profile). The same patch applies to every layer.
- **Tuning guard:** the tasks are split by hash into a dev half (traces read while fixing oriel) and a held-out half (never read). Every result is reported for both.

Latest: round 9 (oriel v12), two runs per layer, 153 tasks (one Chrome task's evaluator failed in every run):

| | oriel v12 | Cua Driver 0.38.0 |
|---|---|---|
| All tasks | 96.2 (63%) | 102.7 (67%) |
| Held-out half (68) | 47.3 | 49.9 |
| Dev half (85) | 48.9 | 52.8 |
| Time per task, median / mean | **49 s / 74 s** | 70 s / 90 s |
| Turns per task | **21.8** | 27.0 |
| Model cost per run | **$38** | $61 |

- **Faster and cheaper:** oriel finishes a task in 30% less time at the median, with 5 fewer model turns, for 38% less money.
- **Completion went to Cua this round:** 6.5 tasks ahead, mostly from Cua's second run (108, its best run in any round; its first scored 98) and LibreOffice Writer (oriel 5 in both runs, Cua 7 and 11).
- **v12 against v11 (round 8):** 96.2 vs 94.2 tasks, 21.8 vs 22.5 turns, the same time and cost. Typing `=SUM(A1:A3)` no longer needs escaping, and a window name that matches nothing lists the open windows.

The first measurement, rounds 4 and 5 (mean of 4 runs per layer, 154 tasks):

| | oriel (v6/v7) | Cua Driver 0.38.0 |
|---|---|---|
| All tasks | 95.5 (62.0%) | 97.9 (63.6%) |
| Held-out half (69) | **44.9** | 43.7 |
| Dev half (85) | 50.6 | 54.2 |
| Model cost per run | **about $41** | about $63 |

- **On par:** the difference is inside the run-to-run noise (a layer's score moves by about 5 tasks between two identical runs). oriel is ahead on the held-out half in both rounds.
- **Cost:** oriel's runs cost about a third less: fewer tokens per task.
- **Round 1 (oriel v1):** 52% against Cua's 61%. The gap closed through general fixes the benchmark exposed:
  - bringing windows to the front from the daemon (its thread had no message queue);
  - keys through paced SendInput (SendKeys dropped modifiers);
  - keys and change reports that survive a hung app;
  - click and drag for cells and canvases;
  - a `wait` that answers once a window goes quiet.
- **Speed then:** both layers took a median of about 78 s per task, set mostly by nested virtualization. On real hardware oriel's calls take milliseconds (section 1).

Per-round tables, per app: [bench/waa/RESULTS.md](../bench/waa/RESULTS.md). Method and rules: [bench/waa/README.md](../bench/waa/README.md).

## 4. Agent task suite (`bench/tasks/`)

An agent (Claude Code headless, Sonnet 5.5) gets one layer's MCP tools and nothing else, plus a task. An independent checker scores the outcome: the file system, WMI, or a separate read of the window.

The tasks are confined to things the suite owns:
- a local web form in the agents' own Chrome profile;
- Character Map;
- File Explorer on a sandbox folder;
- System Information (read only).

It is a small suite on a real PC (no VM), on things it owns, and it was written alongside oriel; WindowsAgentArena (section 3) is the larger, external test.

18 tasks:
- web form: 7 (fill and submit, read, scroll, follow a link, clear, slider, state);
- Character Map: 4 (fill, read the font, read a check box, find Ω with its search);
- File Explorer: 4 (rename, count, delete, new folder);
- System Information: 3 (BIOS, memory, build).

The agent's tools differ per layer, and each layer gets a one-line hint about how its tools work. Competitors get only their accessibility tools (read, click/invoke, set, select, scroll, launch). On a locked PC, keyboard and mouse tools aimed at the foreground would land on the lock screen.

| Tool layer (Sonnet 5.5, same tasks and prompt) | Passed | Median turns | Median time per task | Cost per task |
|---|---|---|---|---|
| **oriel** (2 trials) | **36/36 (100%)** | **5** | **13.8 s** | $0.12 |
| Cua Driver 0.38.0 | 16/18 (89%) | 7.5 | 32.5 s | $0.21 |
| Terminator 0.24.28 | 13/18 (72%) | 6 | 47.5 s | $0.23 |

Failures:
- **Cua Driver:** both full order forms (W1, W2).
- **Terminator:** Character Map's fill and search (C1, C4), and three Explorer tasks (rename, delete, new folder).

Raw rows: `bench/tasks/suite-*.jsonl`.

Two oriel fixes came out of earlier rounds:
- **Round 1, 13/14:** Explorer hides file extensions, so an agent renamed `draft.txt` to `final.txt.txt`. The MCP instructions now say so.
- **An early run:** a link below the fold was invisible to `find`. It now searches off-screen elements too and marks them.
