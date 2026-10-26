# WindowsAgentArena results

Claude Sonnet 5.5 through Claude Code, the same prompt and a 50-turn limit for every layer; only the MCP server changes. WAA's own setup and evaluators score each task (see README.md for the harness and the rules against tuning to the benchmark). Rounds 1-3 ran each task once, so differences of a few tasks there are noise; round 4 ran each task twice.

## Round 9: oriel v12 vs Cua, two runs each, fresh VMs (current)

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

