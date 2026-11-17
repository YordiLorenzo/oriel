# WindowsAgentArena results

Claude Sonnet 5.5 through Claude Code, the same prompt and a 50-turn limit for every layer; only the MCP server changes. WAA's own setup and evaluators score each task (see README.md for the harness and the rules against tuning to the benchmark). Rounds 1-3 ran each task once, so differences of a few tasks there are noise; round 4 ran each task twice.

## How the layers ran

- **Both layers:** the agent has only the layer's MCP server. In rounds 1 to 9, `run.py` removed Claude Code's own tools (Bash, Edit, Write, Read, Glob, Grep, WebFetch, WebSearch, Task, NotebookEdit, TodoWrite) with `--disallowedTools`, which takes a tool out of the model's context ([Claude Code CLI reference](https://code.claude.com/docs/en/cli-reference)). From round 10 on it keeps only ToolSearch, which loads deferred MCP tools (`--tools ToolSearch`). The prompt differs in one sentence only, which names the layer's tools (`ORIEL_HINT` and the Cua hint in `run.py`).
- **oriel:** every tool, as shipped, with the shell off (its default). The oriel + shell layer in round 3 has it on.
- **Cua Driver 0.38.0** ([release](https://github.com/trycua/cua/releases/tag/cua-driver-rs-v0.38.0), published 2026-10-21) in rounds 1 to 9, 0.46.1 in round 10: the Windows x86_64 binary, its SHA-256 checked against the release's `checksums.txt`, telemetry off and the agent cursor overlay off (`serve --no-overlay`), set up by `vm/cua-provision.ps1`. Its daemon refuses clients from the SSH session, so its own stdio MCP proxy runs in the desktop session behind a loopback byte relay (`bench/headtohead/mcp-relay.ps1`).
- **Cua Driver's hidden tools:** 63 tools on Windows ([tool list at 0.38.0](https://github.com/trycua/cua/blob/cua-driver-rs-v0.38.0/.github/scripts/cua-driver-mcp-compat/expected-tools.json)), of which the agent had 59. `run.py` hides four (descriptions from [Cua's 0.38.0 docs](https://github.com/trycua/cua/blob/cua-driver-rs-v0.38.0/docs/content/docs/cua-driver/reference/mcp-tools/batch-recording-and-settings.mdx)):
  - `run_script` runs a JavaScript program over Cua Driver's own tools in one call (loops, branches, retries), in a sandbox with no file, network or process access. It is on by default. It was hidden because oriel has no script tool, so scripting was kept out of the comparison; scripting was measured only for oriel, as oriel + shell in round 3. Cua Driver with `run_script` was not measured in any round.
  - `install_ffmpeg` and `install_extension` install software in the test VM.
  - `check_for_update` checks GitHub for a newer driver; no task needs it, and the version under test stays fixed.
  - All four were hidden in every round. In round 10, every Cua Driver trace of both runs lists 59 Cua Driver tools and none of the four.
- **Newer Cua Driver:** on 2026-11-10 the newest release is [0.46.1](https://github.com/trycua/cua/releases/tag/cua-driver-rs-v0.46.1), published that day. GitHub labels both releases "Pre-release"; Cua's release notes say that label only stops the repository's "Latest" pointer from switching between its separate products, and a plain version number is a stable release. Round 10 ran 0.46.1; versions between the two were not measured.
- **Cost:** each row's `cost_usd` is the `total_cost_usd` that Claude Code reports. Claude Code computes it from the token counts with its list-price table, so it is an estimate, not a bill ([Claude Code docs](https://code.claude.com/docs/en/agent-sdk/cost-tracking)).

## Round 10: oriel 0.1.3 vs Cua Driver 0.46.1, run 1, on firecracker (current)

Set-up as in README.md, "Round 10 on firecracker": a new host and Windows image, effort xhigh, Cua Driver 0.46.1 without the same 4 tools, both layers at the same time. Two runs were made, but only run 1 is a fair measurement: in run 2 the model API refused most calls (below).

151 tasks finished by every layer.

| | oriel 0.1.3, run 1 | Cua Driver 0.46.1, run 1 |
|---|---|---|
| **All** (151) | 110 (73%) | 113 (75%) |
| **Dev half** (84) | 59 (70%) | 64 (76%) |
| **Held-out half** (67) | 51 (76%) | 49 (73%) |
| chrome (16) | 10 | 10 |
| clock (4) | 3 | 3 |
| file_explorer (19) | 16 | 17 |
| libreoffice_calc (24) | 13 | 13 |
| libreoffice_writer (19) | 15 | 14 |
| microsoft_paint (3) | 3 | 3 |
| msedge (13) | 7 | 10 |
| notepad (1) | 1 | 1 |
| settings (5) | 5 | 5 |
| vlc (20) | 16 | 15 |
| vs_code (24) | 18 | 19 |
| windows_calc (3) | 3 | 3 |
| median turns | 24 | 29 |
| median time per task | 49 s | 68 s |
| cost | $22 | $50 |

### Round 10, run 2: not a fair run

In run 2 the model API answered `API Error: Request rejected (429)` to the agents of both layers between 02:31 and 02:47 on 13 Nov 2026 (the traces' write times). The model never ran on those tasks, but `run.py` scored them and wrote a row: it leaves out only the account's "You've hit your" limit reply. `grep -c '"result": "API Error: Request rejected (429)'` counts 85 such rows of oriel's 153 and 95 of Cua Driver's 152; run 1 has none. The run 2 files are kept as they are (`results-oriel-0.1.3-t2.jsonl`, `results-cua-r10-t2.jsonl`), and the figures here use run 1 only, as round 7 did.

### How sure the round 10 differences are

`python3 stats.py --oriel results-oriel-0.1.3-t1.jsonl --cua results-cua-r10-t1.jsonl` (seed 1, 10,000 draws), the same method as round 9 with one run per layer. It compares the 151 tasks that both run 1 files scored: oriel's has no row for one Chrome task (its evaluator returned HTTP 500, as in round 9) and one VLC task (its setup failed); Cua Driver's has none for the same Chrome task and one Notepad task (its scoring failed). Negative means oriel is lower.

| | oriel, all | Cua Driver, all | oriel, feasible | Cua Driver, feasible |
|---|---|---|---|---|
| tasks completed | 109.8 | 112.7 | 101.8 | 105.7 |
| time per task, median (s) | 49 | 68 | 44 | 62 |
| time per task, mean (s) | 61 | 84 | 56 | 78 |
| model cost per run ($) | 21.87 | 50.10 | 18.94 | 44.28 |
| turns per task, mean | 28.8 | 32.9 | 27.7 | 32.2 |

| oriel minus Cua Driver | All 151 tasks | 95% interval | 138 feasible tasks | 95% interval |
|---|---|---|---|---|
| tasks completed | -2.9 | -10.6 to 5.0 | -3.9 | -11.5 to 3.1 |
| completion rate | -1.9 points | -7.0 to 3.3 | -2.8 points | -8.4 to 2.2 |
| time per task, median | -20 s (-29%) | -42% to -14% | -18 s (-29%) | -43% to -14% |
| time per task, mean | -23 s (-27%) | -36% to -18% | -22 s (-28%) | -35% to -19% |
| model cost per run | -$28.23 (-56%) | -60% to -53% | -$25.34 (-57%) | -61% to -53% |
| turns per task, mean | -4.1 (-13%) | -19% to -5% | -4.5 (-14%) | -21% to -6% |

The completion interval reaches zero in both columns, so round 10 shows no completion difference between the layers. The time, cost and turns intervals do not reach zero. With one run per layer there is no run-to-run table. Host load: both layers' run 1 rows ran at a mean 1-minute load of 52 (52.2 and 51.9) and a PSU output of 454 and 453 W (the last table `stats.py` prints), so background load hit both alike.

## Round 9: oriel v12 vs Cua, two runs each, fresh VMs

As round 8. oriel v12: keys types a parenthesis or bracket that no modifier opens as itself (=SUM(A1:A3) used to be refused until escaped), and a window that matches nothing lists the open windows in the error. Tasks cut short by the account's usage limit or by the Mac losing its connection to the VMs were rerun; the Chrome task whose evaluator returns HTTP 500 is left out. Scores are the mean of the two runs.

153 tasks finished in all four runs.

| | oriel v12 | Cua Driver 0.38.0 | oriel run 1 / run 2 | Cua run 1 / run 2 |
|---|---|---|---|---|
| **All** (153) | 96.2 (63%) | 102.7 (67%) | 96 / 97 | 98 / 108 |
| **Dev half** (85) | 48.9 (58%) | 52.8 (62%) | 48 / 50 | 52 / 54 |
| **Held-out half** (68) | 47.3 (70%) | 49.9 (73%) | 48 / 47 | 46 / 54 |
| chrome (16) | 9.9 | 10.4 | 10 / 10 | 10 / 11 |
| clock (4) | 3.0 | 3.0 | 3 / 3 | 3 / 3 |
| file_explorer (19) | 13.5 | 15.0 | 14 / 13 | 16 / 14 |
| libreoffice_calc (24) | 8.5 | 10.5 | 9 / 8 | 10 / 11 |
| libreoffice_writer (19) | 5.0 | 8.9 | 5 / 5 | 7 / 11 |
| microsoft_paint (3) | 2.0 | 2.0 | 2 / 2 | 2 / 2 |
| msedge (13) | 9.5 | 9.0 | 9 / 10 | 8 / 10 |
| notepad (2) | 2.0 | 2.0 | 2 / 2 | 2 / 2 |
| settings (5) | 5.0 | 5.0 | 5 / 5 | 5 / 5 |
| vlc (21) | 16.3 | 15.8 | 16 / 17 | 16 / 16 |
| vs_code (24) | 18.5 | 18.0 | 18 / 19 | 16 / 20 |
| windows_calc (3) | 3.0 | 3.0 | 3 / 3 | 3 / 3 |
| cost per run | $38 | $61 | | |
| time per task, median / mean | 49 s / 74 s | 70 s / 90 s | | |
| turns per task | 21.8 | 27.0 | | |

Run-to-run agreement: oriel gave the same result on 136 of 153 tasks, Cua on 131; the rest flipped between runs, which is the noise a single run carries.

### How sure the round 9 differences are

`python3 stats.py` (seed 1, 10,000 draws) draws the 153 tasks with replacement. Each task drawn keeps both runs of both layers, so every draw compares the two layers on the same tasks. The 95% interval is the middle 95% of the differences. Feasible leaves out the 13 tasks that `tasks.json` marks infeasible. Negative means oriel is lower.

| oriel minus Cua Driver | All 153 tasks | 95% interval | 140 feasible tasks | 95% interval |
|---|---|---|---|---|
| tasks completed (mean of 2 runs) | -6.5 | -12.6 to -0.4 | -7.0 | -13.1 to -1.0 |
| completion rate | -4.2 points | -8.2 to -0.3 | -5.0 points | -9.3 to -0.7 |
| time per task, median | -21 s (-30%) | -36% to -20% | -20 s (-29%) | -34% to -19% |
| time per task, mean | -16 s (-18%) | -26% to -8% | -16 s (-19%) | -27% to -10% |
| model cost per run | -$22.95 (-38%) | -42% to -33% | -$20.35 (-37%) | -41% to -33% |
| turns per task, mean | -5.2 (-19%) | -26% to -12% | -5.1 (-19%) | -26% to -12% |

No interval reaches zero. The same method also finds a gap between Cua Driver's own two runs, which had the same set-up (the third table `stats.py` prints):

| run 2 minus run 1, 153 tasks | oriel | 95% interval | Cua Driver | 95% interval |
|---|---|---|---|---|
| completion rate | 0.7 points | -4.6 to 5.9 | 6.5 points | 1.2 to 12.3 |
| time per task, median | 1% | -13% to 14% | -15% | -31% to -3% |
| time per task, mean | 3% | -9% to 16% | 6% | -6% to 20% |
| model cost | -1% | -6% to 5% | 2% | -5% to 9% |
| turns per task, mean | -3% | -12% to 7% | -4% | -13% to 6% |

So the completion gap between the layers, 4.2 points, is smaller than the gap between Cua Driver's two runs, 6.5 points. The time, cost and turns gaps between the layers are larger than the gap between either layer's two runs. The intervals do not cover a cause that moves a whole run, another model, prompt, Cua Driver version or real hardware. Swapping the layers' input files (`--oriel results-cua-r9-t1.jsonl results-cua-r9-t2.jsonl --cua results-oriel-v12-t1.jsonl results-oriel-v12-t2.jsonl`) flips the sign of every difference.

## Round 8: oriel v11 vs Cua, two runs each, fresh VMs

As round 7, with every VM rebuilt from the warmed image before run 1 and a leftover Windows Firewall prompt closed (Cancel) in the clean slate. oriel v10 and v11: the tools load up front (one model turn fewer), a first lookup in a window reuses the change report's walk, keys checks the focus in one call, and a notification toast holding the foreground no longer blocks keys. Run 2 was rerun from the start after the Mac running the agents lost its connection; a task whose scoring failed in both runs (a Chrome evaluator returning HTTP 500) is left out. Scores are the mean of the two runs.

153 tasks finished in all four runs.

| | oriel v11 | Cua Driver 0.38.0 | oriel run 1 / run 2 | Cua run 1 / run 2 |
|---|---|---|---|---|
| **All** (153) | 94.2 (62%) | 97.0 (63%) | 94 / 95 | 98 / 96 |
| **Dev half** (85) | 48.4 (57%) | 52.0 (61%) | 49 / 48 | 52 / 52 |
| **Held-out half** (68) | 45.8 (67%) | 45.0 (66%) | 45 / 47 | 46 / 44 |
| chrome (16) | 9.4 | 9.9 | 9 / 10 | 10 / 10 |
| clock (4) | 3.0 | 3.0 | 3 / 3 | 3 / 3 |
| file_explorer (19) | 15.0 | 15.0 | 15 / 15 | 16 / 14 |
| libreoffice_calc (24) | 7.5 | 8.5 | 7 / 8 | 8 / 9 |
| libreoffice_writer (19) | 7.0 | 7.5 | 7 / 7 | 8 / 7 |
| microsoft_paint (3) | 2.0 | 2.0 | 2 / 2 | 2 / 2 |
| msedge (13) | 6.5 | 8.0 | 6 / 7 | 8 / 8 |
| notepad (2) | 2.0 | 2.0 | 2 / 2 | 2 / 2 |
| settings (5) | 5.0 | 5.0 | 5 / 5 | 5 / 5 |
| vlc (21) | 15.8 | 14.1 | 16 / 16 | 15 / 13 |
| vs_code (24) | 19.0 | 19.0 | 20 / 18 | 18 / 20 |
| windows_calc (3) | 2.0 | 3.0 | 2 / 2 | 3 / 3 |
| cost per run | $38 | $60 | | |
| time per task, median / mean | 51 s / 73 s | 64 s / 82 s | | |
| turns per task | 22.5 | 26.2 | | |

Run-to-run agreement: oriel gave the same result on 139 of 153 tasks, Cua on 132; the rest flipped between runs, which is the noise a single run carries.

## Round 6: oriel v8 vs Cua, two runs each

As round 5, with Windows Update, its orchestrator, search indexing and SysMain off in every VM from the start. oriel v8: the fallbacks for hung apps also catch the TimeoutException form of UIA timeouts (they had been skipped), Tab/Enter then text allowed in spreadsheet cells. Scores are the mean of the two runs.

152 tasks finished in all four runs.

| | oriel v8 | Cua Driver 0.38.0 | oriel run 1 / run 2 | Cua run 1 / run 2 |
|---|---|---|---|---|
| **All** (152) | 93.7 (62%) | 96.7 (64%) | 92 / 96 | 94 / 100 |
| **Dev half** (84) | 49.9 (59%) | 51.9 (62%) | 48 / 52 | 52 / 52 |
| **Held-out half** (68) | 43.8 (64%) | 44.9 (66%) | 44 / 44 | 42 / 48 |
| chrome (17) | 9.4 | 8.9 | 8 / 11 | 7 / 11 |
| clock (4) | 3.0 | 3.0 | 3 / 3 | 3 / 3 |
| file_explorer (19) | 14.0 | 15.0 | 14 / 14 | 15 / 15 |
| libreoffice_calc (24) | 7.5 | 9.5 | 8 / 7 | 9 / 10 |
| libreoffice_writer (19) | 8.4 | 8.5 | 8 / 9 | 9 / 8 |
| microsoft_paint (3) | 2.0 | 2.0 | 2 / 2 | 2 / 2 |
| msedge (13) | 9.0 | 8.5 | 9 / 9 | 8 / 9 |
| notepad (2) | 1.5 | 2.0 | 1 / 2 | 2 / 2 |
| settings (5) | 5.0 | 5.0 | 5 / 5 | 5 / 5 |
| vlc (20) | 14.4 | 15.4 | 15 / 14 | 15 / 16 |
| vs_code (23) | 17.5 | 16.0 | 17 / 18 | 16 / 16 |
| windows_calc (3) | 2.0 | 3.0 | 2 / 2 | 3 / 3 |
| cost per run | $40 | $61 | | |
| time per task, median / mean | 72 s / 93 s | 72 s / 111 s | | |
| turns per task | 25.4 | 27.0 | | |

Run-to-run agreement: oriel gave the same result on 131 of 152 tasks, Cua on 136; the rest flipped between runs, which is the noise a single run carries.

## Round 5: oriel v7 vs Cua, two runs each

As round 4, and the clean slate before each task also clears LibreOffice's recovery list (killing it had made the next start open "Document Recovery" in a fifth of round 4's tasks). oriel v7: wait answers once a window has gone quiet, faster typing, server instructions that cover keys, click and drag. Scores are the mean of the two runs.

154 tasks finished in all four runs.

| | oriel v7 | Cua Driver 0.38.0 | oriel run 1 / run 2 | Cua run 1 / run 2 |
|---|---|---|---|---|
| **All** (154) | 90.8 (59%) | 95.9 (62%) | 88 / 94 | 92 / 100 |
| **Dev half** (85) | 48.0 (56%) | 54.5 (64%) | 44 / 52 | 53 / 56 |
| **Held-out half** (69) | 42.9 (62%) | 41.4 (60%) | 44 / 42 | 39 / 44 |
| chrome (17) | 6.4 | 8.4 | 6 / 7 | 8 / 9 |
| clock (4) | 2.0 | 3.0 | 2 / 2 | 3 / 3 |
| file_explorer (19) | 14.0 | 15.0 | 14 / 14 | 15 / 15 |
| libreoffice_calc (24) | 7.5 | 8.5 | 8 / 7 | 7 / 10 |
| libreoffice_writer (19) | 7.0 | 7.5 | 5 / 9 | 7 / 8 |
| microsoft_paint (3) | 2.0 | 2.0 | 2 / 2 | 2 / 2 |
| msedge (13) | 8.5 | 9.5 | 9 / 8 | 9 / 10 |
| notepad (2) | 2.0 | 1.5 | 2 / 2 | 2 / 1 |
| settings (5) | 5.0 | 5.0 | 5 / 5 | 5 / 5 |
| vlc (21) | 15.4 | 15.5 | 16 / 15 | 15 / 16 |
| vs_code (24) | 18.5 | 17.5 | 17 / 20 | 17 / 18 |
| windows_calc (3) | 2.5 | 2.5 | 2 / 3 | 2 / 3 |
| cost per run | $41 | $63 | | |
| time per task, median / mean | 69 s / 93 s | 67 s / 89 s | | |
| turns per task | 26.2 | 27.4 | | |

Run-to-run agreement: oriel gave the same result on 126 of 154 tasks, Cua on 128; the rest flipped between runs, which is the noise a single run carries.

## Round 4: oriel v6 vs Cua, two runs each

The warmed image as in round 3, plus a clean slate before every task (LibreOffice and "Pick an app" dialogs closed, as WAA's own setup gets by reverting the VM). oriel v6: keys through paced SendInput (modifiers no longer dropped) and keys to hung apps. Scores are the mean of the two runs.

154 tasks finished in all four runs.

| | oriel v6 | Cua Driver 0.38.0 | oriel run 1 / run 2 | Cua run 1 / run 2 |
|---|---|---|---|---|
| **All** (154) | 100.2 (65%) | 99.9 (65%) | 101 / 100 | 97 / 103 |
| **Dev half** (85) | 53.3 (63%) | 54.0 (63%) | 55 / 52 | 53 / 55 |
| **Held-out half** (69) | 46.9 (68%) | 46.0 (67%) | 46 / 48 | 44 / 48 |
| chrome (17) | 7.9 | 7.9 | 8 / 8 | 7 / 9 |
| clock (4) | 2.5 | 3.0 | 3 / 2 | 3 / 3 |
| file_explorer (19) | 14.5 | 14.0 | 14 / 15 | 14 / 14 |
| libreoffice_calc (24) | 11.5 | 10.0 | 12 / 11 | 8 / 12 |
| libreoffice_writer (19) | 8.4 | 11.0 | 8 / 9 | 11 / 11 |
| microsoft_paint (3) | 2.0 | 2.0 | 2 / 2 | 2 / 2 |
| msedge (13) | 9.0 | 9.5 | 9 / 9 | 9 / 10 |
| notepad (2) | 1.5 | 2.0 | 2 / 1 | 2 / 2 |
| settings (5) | 5.0 | 5.0 | 5 / 5 | 5 / 5 |
| vlc (21) | 16.4 | 15.5 | 17 / 16 | 16 / 15 |
| vs_code (24) | 19.0 | 17.0 | 19 / 19 | 17 / 17 |
| windows_calc (3) | 2.5 | 3.0 | 2 / 3 | 3 / 3 |
| cost per run | $43 | $66 | | |
| time per task, median / mean | 81 s / 104 s | 80 s / 96 s | | |
| turns per task | 28.2 | 28.3 | | |

Run-to-run agreement: oriel gave the same result on 140 of 154 tasks, Cua on 127; the rest flipped between runs, which is the noise a single run carries.

## Round 3: warmed image, oriel v4

LibreOffice started once before saving the image (its first-run updater done), the Chrome/Edge profile patch, WAA's server hidden. oriel v4: foregrounding fixed, click and drag, keys fallback for hung apps.

154 tasks finished by every layer.

| | oriel v4 | Cua Driver 0.38.0 | oriel v4 + shell |
|---|---|---|---|
| **All** (154) | 91 (59%) | 98 (64%) | 94 (61%) |
| **Dev half** (85) | 47 (55%) | 54 (63%) | 51 (60%) |
| **Held-out half** (69) | 44 (64%) | 44 (64%) | 43 (62%) |
| chrome (17) | 10 | 9 | 8 |
| clock (4) | 3 | 3 | 3 |
| file_explorer (19) | 14 | 14 | 15 |
| libreoffice_calc (24) | 9 | 9 | 9 |
| libreoffice_writer (19) | 6 | 10 | 7 |
| microsoft_paint (3) | 2 | 2 | 2 |
| msedge (13) | 9 | 8 | 9 |
| notepad (2) | 0 | 1 | 0 |
| settings (5) | 5 | 5 | 5 |
| vlc (21) | 15 | 15 | 17 |
| vs_code (24) | 15 | 21 | 16 |
| windows_calc (3) | 3 | 1 | 3 |
| median turns | 25 | 24 | 24 |
| median time per task | 90 s | 78 s | 88 s |
| cost | $44 | $65 | $43 |

## Round 1: cold image, oriel v1

Every fresh VM ran LibreOffice's updater on first launch (minutes, sometimes looping); the oriel run was stopped at 116 tasks.

116 tasks finished by every layer.

| | oriel v1 | Cua Driver 0.38.0 |
|---|---|---|
| **All** (116) | 60 (52%) | 71 (61%) |
| **Dev half** (61) | 31 (51%) | 37 (61%) |
| **Held-out half** (55) | 29 (53%) | 34 (62%) |
| chrome (14) | 8 | 8 |
| file_explorer (19) | 15 | 15 |
| libreoffice_calc (24) | 6 | 10 |
| libreoffice_writer (19) | 4 | 7 |
| microsoft_paint (3) | 1 | 2 |
| msedge (6) | 4 | 4 |
| notepad (2) | 2 | 1 |
| settings (5) | 5 | 5 |
| vlc (6) | 3 | 5 |
| vs_code (17) | 11 | 13 |
| windows_calc (1) | 1 | 1 |
| median turns | 28 | 27 |
| median time per task | 131 s | 97 s |
| cost | $34 | $54 |

## oriel v2 on the cold image

Fixes before the foreground bug was found; stopped at 101 tasks.

88 tasks finished by every layer.

| | oriel v2 | oriel v1 |
|---|---|---|
| **All** (88) | 43 (49%) | 44 (50%) |
| **Dev half** (47) | 20 (43%) | 22 (47%) |
| **Held-out half** (41) | 23 (56%) | 22 (53%) |
| chrome (12) | 8 | 7 |
| file_explorer (19) | 13 | 15 |
| libreoffice_calc (23) | 3 | 5 |
| libreoffice_writer (12) | 5 | 3 |
| microsoft_paint (3) | 1 | 1 |
| msedge (3) | 1 | 1 |
| notepad (2) | 1 | 2 |
| settings (5) | 5 | 5 |
| vlc (3) | 2 | 1 |
| vs_code (6) | 4 | 4 |
| median turns | 36 | 30 |
| median time per task | 167 s | 138 s |
| cost | $27 | $25 |

