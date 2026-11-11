# Benchmarks

Sections 1, 2 and 4 ran on one Windows 11 Pro PC (build 26200, Ryzen 5 7600X, 32 GB), driven from a MacBook over a gigabit LAN (about 3 ms round trip). On 2026-10-21/22 that PC was **locked**, which rules out keyboard input and suspends Store apps (Calculator, Settings), so those aren't measured there. On 2026-11-10 it was unlocked (section 2). Section 3 ran in WindowsAgentArena's own Windows 11 VMs, unlocked.

The scripts, and how to run them on your own PC, are in [bench/README.md](../bench/README.md).

## Summary

| Benchmark | What it measures | Result |
|---|---|---|
| [3. WindowsAgentArena](#3-windowsagentarena-benchwaa) | Whole tasks: Microsoft's 153 scored tasks in a Windows 11 VM, the same model on both sides, only the MCP server swapped | Round 9 (October 2026), oriel v12 against Cua Driver 0.38.0 without 4 of its 63 tools (`run_script` among them): a median 49 s per task against 70 s, 21.8 model turns against 27.0, and $38 against $61 per run in model cost as Claude Code estimates it. Each of these gaps holds at 95% confidence and is larger than either layer's own run-to-run gap. Cua Driver completed more tasks: 67% against 63%. Its own two runs differed by 6.5 points. |
| [2. Head-to-head](#2-head-to-head-benchheadtohead) | Single MCP calls on the same PC | 2026-11-10, oriel 0.1.0 against Cua Driver 0.46.1 and agent-desktop 1.2.0: oriel took less time on every call measured. Listing windows 2 ms against 31 and 44 ms, launching an app 193 ms against 605 and 251 ms, an action 2 ms against 3 to 6 and 174 to 187 ms. |
| [1. Latency per call](#1-latency-per-call-benchlatencypy) | oriel's own time per call, over SSH (2026-10-21/22, PC locked) | Most calls take 5 to 60 ms. |
| [4. Agent task suite](#4-agent-task-suite-benchtasks) | 18 tasks written alongside oriel (2026-10-21/22, PC locked) | oriel passed 36 of 36 runs, Cua Driver 0.38.0 16 of 18. WindowsAgentArena is the larger, external test. |

## What this does not show

Read these before you quote a number from this page.

- **Which feature saved the time.** Swapping the server cut time, turns and cost in round 9. The benchmark does not show which part of oriel did it.
- **That oriel completes as many tasks.** Cua Driver 0.38.0 completed 4.2 points more in round 9 (67% against 63%). Its own two runs, with the same set-up, differed by 6.5 points (98 and 108 of 153 tasks), so the gap is within run-to-run variation. But its lower run still beat oriel's higher run (97), and it completed more in every round since round 5 ([every round](#every-round)).
- **That the infeasible tasks cause the completion gap.** On the 140 tasks that `tasks.json` marks feasible, it is 63% against 68%, so the gap is there without the 13 infeasible tasks too. Most of it is LibreOffice Writer: 5 against 8.9 of 19 tasks.
- **That the time gap comes from failing sooner.** On the 77 tasks that all four round 9 runs completed, oriel still took a median 46 s against 63 s, with 17.8 against 23.7 turns per task.
- **Cua Driver with all its tools.** It ran without 4 of its 63 tools. `run_script` runs JavaScript over Cua Driver's own tools in a sandbox with no file, network or process access. It was hidden because oriel has no script tool. With it, Cua Driver might need fewer turns, less time and a lower cost. `install_ffmpeg` and `install_extension` install software, and `check_for_update` checks GitHub for a newer driver ([details](#what-each-layer-could-use)).
- **The current Cua Driver on whole tasks.** WindowsAgentArena ran Cua Driver 0.38.0 (released 2026-10-21). The newest on 2026-11-10 is 0.46.1. Only the per-call test (section 2) ran 0.46.1; no WindowsAgentArena round has.
- **Money spent.** Model cost is the `total_cost_usd` that Claude Code reports: its list-price estimate from the token counts, not a bill. The runs used one Claude account, which hit its usage limit in rounds 7 and 9; the tasks it cut short were rerun.
- **Other set-ups.** One model (Claude Sonnet 5.5 in Claude Code), one prompt, nested VMs on one droplet. Not other models, other clients or real hardware.
- **Other tools.** oriel was not measured against Claude Desktop's or ChatGPT's computer use. agent-desktop 1.2.0 ran only in the per-call test (section 2), not on WindowsAgentArena.

## 1. Latency per call (`bench/latency.py`)

oriel's daemon as it stood on 2026-10-21/22, PC locked, reached over SSH the way `ssh pc oriel mcp` reaches it. The table gives the median wall time on the Mac; the network share is about 5 ms.

| Step | Time |
|---|---|
| ping (the floor: SSH and network) | 5-8 ms |
| list windows | 6 ms |
| press a button, by name or ref | 4-6 ms |
| fill a field, by partial name | 16-18 ms |
| read a field | 16 ms |
| press + change report | 40-55 ms |
| tree: Character Map / Chrome / Edge | 22-28 / 28-30 / 38-40 ms |
| tree: File Explorer (settled / just opened) | 52-56 / 190-215 ms |
| launch Character Map (+ its tree, one call) | 190-200 (266-300) ms |
| a typo, answered with the closest names | 840 ms (0.8 s of retries while controls may still be appearing) |

## 2. Head-to-head (`bench/headtohead/`)

The same tasks through each MCP server on the same PC. Each server ran in the desktop session, and each client ran on the Mac with that tool's speed settings (no screenshots, highlighting or post-action diffs). The tables give the median wall time per MCP call. Rows, method and the browser trees: [bench/headtohead/RESULTS.md](../bench/headtohead/RESULTS.md).

### 2026-11-10, second run: agent-desktop 1.2.0 added (current)

agent-desktop 1.2.0 (vercel-labs, released 2026-11-05, its first release with an MCP server) beside oriel 0.1.0 and Cua Driver 0.46.1, all three in one session on the same PC, unlocked and idle. 5 runs per task. agent-desktop ran as `agent-desktop mcp` with its default headless actions and the `snapshot -i` loop from its quick start.

| Task | oriel 0.1.0 | Cua Driver 0.46.1 | agent-desktop 1.2.0 |
|---|---|---|---|
| List windows | **2 ms** | 31 ms | 44 ms |
| Launch Character Map | **193 ms** | 605 ms | 251 ms |
| Launch + return its tree | **294 ms**, 1 call | 2 calls | 2 calls |
| Character Map tree | **21 ms** | 37 ms (+ 30 ms list_windows) | 143 ms |
| Fill a field (ref / token) | **2 ms** | 3 ms | 174 ms |
| Press a button (ref / token) | **2 ms** | 6 ms | 187 ms |
| Press + report what changed | **44 ms**, 1 call | 2 calls | 2 calls |
| Correct outcome | 10/10 | 5/5 | 5/5 |

- **agent-desktop** launches faster than Cua Driver, but each Character Map call takes 143 to 187 ms against 2 to 37 ms. On the browser pages each tree took 2.4 s (its skeleton) to 3.1 s (`-i`, which stops at its 3 s deadline and returns a partial tree), against about 0.1 s for oriel and 0.2 s for Cua Driver. Each tool stops at its own default limit, so those trees are not the same size.
- **Cua Driver 0.46.1** was within 4 ms of the first run below on every row.

### 2026-11-10: oriel 0.1.0 against Cua Driver 0.46.1 (first run)

Cua Driver 0.46.1 was the newest release that day. The PC was unlocked and idle. 5 runs per task. Terminator and Windows-MCP were not run again.

| Task | oriel 0.1.0 | Cua Driver 0.46.1 | Cua Driver 0.38.0, same day |
|---|---|---|---|
| List windows | **1 ms** | 30 ms | 40 ms |
| Launch Character Map | **192 ms** (waits for the window) | 604 ms | 619 ms |
| Launch + return its tree | **294 ms**, 1 call | 2 calls | 2 calls |
| Character Map tree | **20 ms** | 37 ms (+ 30 ms list_windows to find the window) | 40 ms |
| Fill a field (ref / token) | **2 ms** | 3 ms | 3 ms |
| Press a button (ref / token) | **2 ms** | 5 ms | 7 ms |
| Press + report what changed | **42 ms**, 1 call | 2 calls | 2 calls |
| Correct outcome | 10/10 | 5/5 | 5/5 |

Cua Driver ran through its daemon (`serve --no-overlay`, then `mcp --socket`). Bare `cua-driver mcp` shows its agent cursor by default, which adds about 565 ms to each fill and press; with the cursor off it matches the daemon within 2 ms. The browser trees are left out of this table: each tool stops at its own default limit, so they don't read the same amount of the page.

**Reading it:**
- **Per action,** both take a few ms: oriel 2 ms, Cua Driver 3 to 5 ms.
- **The gaps that remain:** listing windows (1 against 30 ms; Cua Driver needs that call to find a window), launching (192 against 604 ms), reading a window (20 against 37 ms), and launch + tree and press + report, which take Cua Driver two calls.
- **Cua Driver's version is not what changed.** Cua Driver 0.38.0, run in the same session, was within 15 ms of 0.46.1 on every row. So the larger gaps of 2026-10-21 (501 ms to list windows, 902 ms to launch) came from that day's conditions, most likely the locked PC. That is an inference: the open windows differed too.

### 2026-10-21: four servers, PC locked (history)

Kept as history. Cua Driver's figures here came from a locked PC and differ from the same version's figures on an unlocked PC (above), so use the 2026-11-10 tables for Cua Driver. Terminator and Windows-MCP were measured only on this day. 5 runs per task (3 for Windows-MCP); medians:

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
- **Terminator** takes seconds per call, even with its default per-call screenshot turned off.
- **Windows-MCP** sees the screen through a whole-desktop snapshot, which takes 14 s.

## 3. WindowsAgentArena (`bench/waa/`)

[WindowsAgentArena](https://github.com/microsoft/WindowsAgentArena) is Microsoft's 154-task benchmark in a real Windows 11 VM:
- Chrome, Edge, File Explorer;
- LibreOffice Calc and Writer, VS Code, VLC;
- Settings, Paint, Clock, Calculator, Notepad.

The harness keeps WAA's own setup and evaluators for every task and swaps only the agent's tools:
- **Agent:** Claude Sonnet 5.5 through Claude Code, the same prompt and turn limit (50) for every layer. The prompt differs in one sentence only, which names the layer's tools (quoted below).
- **Hosting:** one DigitalOcean droplet, 10 VMs from round 7 on (14 before).
- **Patches:** WAA no longer runs as published, so `host/waa-patch.py` repairs its setup (dead LibreOffice mirror, hanging Caddy download, missing VC++ runtime, Chrome 136+ ignoring remote debugging on the default profile). The same patch applies to every layer.
- **Scoring:** `waa_env.py` scores each task with `float(env.evaluate())`. WAA's `show_result.py`, which counts a result it cannot parse as a pass ([WAA issue #86](https://github.com/microsoft/WindowsAgentArena/issues/86)), is not used.
- **Tuning guard:** the tasks are split by hash into a dev half (traces read while fixing oriel) and a held-out half (never read). Every result is reported for both.
- **Model cost** is the `total_cost_usd` that Claude Code reports for each task. Claude Code computes it from the token counts with its list-price table, so it is an estimate, not a bill ([Claude Code docs](https://code.claude.com/docs/en/agent-sdk/cost-tracking)). Both layers are priced with the same table.

### What each layer could use

The agent had only the layer's MCP server. For both layers, Claude Code's own tools (Bash, Read, Edit, Write, Glob, Grep, WebFetch, WebSearch, Task, NotebookEdit, TodoWrite) were removed. The one sentence of the prompt that differs (`bench/waa/run.py`):
- **oriel:** "The oriel tools read windows as text (tree, find) and act on elements by ref (invoke, set, select, scroll, keys)."
- **Cua Driver:** "Cua Driver reads a window with get_window_state (include_screenshot false keeps it fast) and acts on element tokens (click, set_value), with keyboard and mouse tools too."

What each layer had:
- **oriel:** every tool, as shipped. Its shell tool is off by default and stayed off. Round 3 also ran oriel with the shell on (`bench/waa/RESULTS.md`).
- **Cua Driver 0.38.0** ([release](https://github.com/trycua/cua/releases/tag/cua-driver-rs-v0.38.0), 2026-10-21): the Windows x86_64 build, checksum checked against the release, telemetry off, the agent cursor overlay off (`serve --no-overlay`). The agent had 59 of its 63 tools ([tool list at 0.38.0](https://github.com/trycua/cua/blob/cua-driver-rs-v0.38.0/.github/scripts/cua-driver-mcp-compat/expected-tools.json)). `run.py` removed four from the model's context with Claude Code's `--disallowedTools` ([CLI reference](https://code.claude.com/docs/en/cli-reference)), in every round:

| Hidden tool | What it does ([Cua Driver 0.38.0 docs](https://github.com/trycua/cua/blob/cua-driver-rs-v0.38.0/docs/content/docs/cua-driver/reference/mcp-tools/batch-recording-and-settings.mdx)) | Why it was hidden |
|---|---|---|
| `run_script` | Runs a JavaScript program over Cua Driver's own tools in one call, with loops, branches and retries. It runs in a sandbox with no file, network or process access. It is on by default. | oriel has no script tool, so scripting was kept out of the comparison. Scripting was measured only for oriel, with its shell on (round 3). |
| `install_ffmpeg` | Installs ffmpeg for screen recording. | It installs software in the test VM. |
| `install_extension` | Installs an optional driver extension. | It installs software in the test VM. |
| `check_for_update` | Checks GitHub for a newer driver. | No task needs it, and the version under test stays fixed. |

Of the four, only `run_script` could help with a task. Cua Driver with `run_script` was not measured in any round. It might lower Cua Driver's turns, time and cost, because a loop over rows becomes one call. Cua Driver's `run_steps` (several actions in one call) stayed available, as did oriel's `batch`.

The newest Cua Driver on 2026-11-10 is [0.46.1](https://github.com/trycua/cua/releases/tag/cua-driver-rs-v0.46.1), released that day. On WindowsAgentArena, versions after 0.38.0 were not measured; the per-call head-to-head (section 2) was rerun with 0.46.1 on 2026-11-10.

### Round 9

Latest: round 9 (oriel v12, October 2026), two runs per layer, 153 of WAA's 154 tasks (one Chrome task's evaluator failed in every run). Completion is the mean of the two runs.

| | oriel v12 | Cua Driver 0.38.0 |
|---|---|---|
| All tasks (153) | 96.2 (63%) | **102.7 (67%)** |
| Each run | 96 / 97 | 98 / 108 |
| Feasible tasks (140) | 88.7 (63%) | **95.7 (68%)** |
| Held-out half (68) | 47.3 | **49.9** |
| Dev half (85) | 48.9 | **52.8** |
| LibreOffice Writer (19) | 5.0 | **8.9** |
| Time per task, median / mean | **49 s / 74 s** | 70 s / 90 s |
| Turns per task, mean | **21.8** | 27.0 |
| Model cost per run (list-price estimate) | **$38** | $61 |

- **Time, turns and cost:** with oriel, tasks took a median 49 s against 70 s (mean 74 s against 90 s), with 5.2 fewer model turns and 38% lower estimated model cost.
- **Completion:** Cua Driver completed 6.5 tasks more (4.2 points). Its two runs scored 98 and 108; 108 is its highest run in any round, and its lower run still beat oriel's higher run (97). Most of the gap is LibreOffice Writer (oriel 5 in both runs, Cua Driver 7 and 11).
- **v12 against v11 (round 8):** 96.2 against 94.2 tasks, 21.8 against 22.5 turns, a median 49 s against 51 s per task, and the same cost ($38 per run). Typing `=SUM(A1:A3)` no longer needs escaping, and a window name that matches nothing lists the open windows.

### How sure the round 9 differences are

![WindowsAgentArena round 9, 153 tasks, two runs per tool layer. oriel's difference from Cua Driver with 95% intervals: time per task 30% lower (20% to 36%), model cost 38% lower (33% to 42%), turns per task 19% lower (12% to 26%). Tasks completed: oriel 62.6% and 63.2% in its two runs, Cua Driver 63.9% and 70.4%.](images/benchmark-round9.png)

`bench/waa/stats.py` draws the 153 tasks with replacement 10,000 times (seed 1). Each task drawn keeps both runs of both layers, so every draw compares the two layers on the same tasks. The 95% interval is the middle 95% of the 10,000 differences. The feasible columns leave out the 13 tasks that `tasks.json` marks infeasible. A negative difference means oriel is lower.

| oriel minus Cua Driver | All 153 tasks | 95% interval | 140 feasible tasks | 95% interval |
|---|---|---|---|---|
| Completion | -4.2 points (-6.5 tasks) | -8.2 to -0.3 points | -5.0 points (-7.0 tasks) | -9.3 to -0.7 points |
| Time per task, median | -30% (-21 s) | -36% to -20% | -29% (-20 s) | -34% to -19% |
| Time per task, mean | -18% (-16 s) | -26% to -8% | -19% (-16 s) | -27% to -10% |
| Model cost per run | -38% (-$22.95) | -42% to -33% | -37% (-$20.35) | -41% to -33% |
| Turns per task | -19% (-5.2) | -26% to -12% | -19% (-5.1) | -26% to -12% |

The same method compares each layer's run 2 with its run 1, with the same set-up:

| Run 2 minus run 1, 153 tasks | oriel | 95% interval | Cua Driver | 95% interval |
|---|---|---|---|---|
| Completion | 0.7 points | -4.6 to 5.9 | 6.5 points | 1.2 to 12.3 |
| Time per task, median | 1% | -13% to 14% | -15% | -31% to -3% |
| Time per task, mean | 3% | -9% to 16% | 6% | -6% to 20% |
| Model cost | -1% | -6% to 5% | 2% | -5% to 9% |
| Turns per task | -3% | -12% to 7% | -4% | -13% to 6% |

- **Time, cost and turns:** oriel's lower figures hold in every interval, with or without the infeasible tasks. Each gap is also larger than the gap between either layer's own two runs (at most 15% for time, 2% for cost, 4% for turns).
- **Completion:** on these tasks with this model, Cua Driver 0.38.0 without `run_script` completed more tasks than oriel v12. Its interval, 0.3 to 8.2 points in Cua Driver's favour, does not reach zero. But Cua Driver's own second run was 6.5 points above its first (95% interval 1.2 to 12.3), and that interval does not reach zero either. The completion gap between the layers, 4.2 points, is smaller than that.
- **Tasks both layers completed:** on the 77 tasks that scored 1 in all four runs, oriel took a median 46 s against 63 s, with 17.8 against 23.7 turns per task (from the four round 9 `results-*.jsonl` files). So the time gap is not only oriel failing sooner.
- **What the intervals cover:** which tasks happen to be in the benchmark, and the variation between the two runs of each task. They do not cover a cause that moves a whole run, another model, another prompt, a newer Cua Driver or real hardware.

### Every round

Cua Driver 0.38.0 in every round, without the same 4 tools. Completion is the mean of the runs.

| Round | oriel | Tasks | Runs per layer | Completed, oriel / Cua Driver | Median time per task, oriel / Cua Driver | Model cost per run, oriel / Cua Driver |
|---|---|---|---|---|---|---|
| 1 (cold image) | v1 | 116 | 1 | 52% / 61% | 131 s / 97 s | $34 / $54 |
| 3 | v4 | 154 | 1 | 59% / 64% | 90 s / 78 s | $44 / $65 |
| 4 | v6 | 154 | 2 | 65% / 65% | 81 s / 80 s | $43 / $66 |
| 5 | v7 | 154 | 2 | 59% / 62% | 69 s / 67 s | $41 / $63 |
| 6 | v8 | 152 | 2 | 62% / 64% | 72 s / 72 s | $40 / $61 |
| 7, run 1 only | v9 | 153 | 1 | 59% / 66% | 74 s / 72 s | $42 / $62 |
| 8 | v11 | 153 | 2 | 62% / 63% | 51 s / 64 s | $38 / $60 |
| 9 | v12 | 153 | 2 | 63% / 67% | 49 s / 70 s | $38 / $61 |

- **Completion:** Cua Driver completed more in every round since round 5. In round 4 the two were level: oriel 100.2 and Cua Driver 99.9 tasks.
- **Time:** the median time gap appeared in round 8 (oriel v11, every VM rebuilt before the run); the benchmark does not show which change caused it. In rounds 1 to 7, oriel's median was the same as Cua Driver's or longer (34 s longer in round 1).
- **Cost:** oriel's estimated model cost per run was about a third lower in every round.
- **Round 2:** RESULTS.md has no round 2 with Cua Driver. Its "oriel v2 on the cold image" table compares oriel v2 with oriel v1 only.
- **Round 7:** only run 1 was a fair run. Run 2 (oriel v10) hit the account's weekly limit and was rerun after the VMs had sat idle for about 20 hours. In the rerun, a firewall prompt or a notification toast held the foreground on some VMs. That blocked oriel's keys on 38 of 72 dev tasks, while Cua Driver typed on ([environment notes](../bench/waa/README.md#environment-notes-per-round)). Run 2 scored 85.7 against 95.7 of 154 tasks. RESULTS.md does not show round 7; the figures come from `results-oriel-v9-t1.jsonl` and `results-cua-r7-t1.jsonl` (run 1), and `results-oriel-v10-t1.jsonl` and `results-cua-r7-t2.jsonl` (run 2).
- **Round 1 (oriel v1):** 52% against 61%. The gap closed through general fixes the benchmark exposed:
  - bringing windows to the front from the daemon (its thread had no message queue);
  - keys through paced SendInput (SendKeys dropped modifiers);
  - keys and change reports that survive a hung app;
  - click and drag for cells and canvases;
  - a `wait` that answers once a window goes quiet.

Per-round tables, per app: [bench/waa/RESULTS.md](../bench/waa/RESULTS.md). Method and rules: [bench/waa/README.md](../bench/waa/README.md).

## 4. Agent task suite (`bench/tasks/`)

An agent (Claude Code headless, Sonnet 5.5) gets one layer's MCP tools and nothing else, plus a task. An independent checker scores the outcome: the file system, WMI, or a separate read of the window. It ran on 2026-10-21/22, PC locked.

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
