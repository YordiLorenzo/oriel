# WindowsAgentArena with a swapped tool layer

[WindowsAgentArena](https://github.com/microsoft/WindowsAgentArena) (WAA) is Microsoft's benchmark of 154 tasks in a real Windows 11 VM: Chrome, Edge, File Explorer, LibreOffice Calc and Writer, VS Code, VLC, Settings, Paint, Clock, Calculator and Notepad.

This harness keeps WAA's own per-task setup and scoring and replaces only the agent:
- **Setup and scoring:** `waa_env.py` runs WAA's `DesktopEnv.reset()` (close apps, run the task's setup) and `evaluate()` unchanged, inside the WAA container.
- **The agent:** Claude Code headless, given the task's instruction and one computer-use MCP server running inside the VM. It gets no other tools: no shell, no file access, no WAA observations.
- **Infeasible tasks:** the agent ends with `INFEASIBLE` to claim one, which is passed to WAA as its `FAIL` action.

Every tool layer gets the same model, prompt and turn limit (50); only the MCP server changes.

## Layout

| Path | What |
|---|---|
| `run.py` | Runs on the client computer (a Mac in rounds 1 to 9, the WAA host itself in round 10). For each task: WAA reset, agent run, WAA evaluate, then one JSON row. Resumes from its output file. |
| `waa_env.py` | Runs in the WAA container (`/client`). Calls WAA's setup and scoring. |
| `host/` | Scripts for the WAA host: the WSL2 distro `waa` on a Windows PC (`waa-*`), or a Linux machine with KVM, a DigitalOcean droplet or the lab's firecracker (`do-*`, `waa-tools.sh`, `agent-home.sh`). They live in `$WAA_HOME` (default `/root`). |
| `vm/` | Scripts that run inside the WAA VM, from the share (`\\host.lan\Data` = WAA's `vm/setup`). |

## Setup on a Windows PC with WSL2

The first setup, tested on Windows 11 with an AMD Ryzen CPU. Copy `host/` to one folder on a local drive of the PC.

1. **KVM in WSL.**
   - `.wslconfig` needs `nestedVirtualization=true`.
   - The October 2026 cumulative update (KB5127648, build 26200.9711) turns off nested virtualization on AMD CPUs ([microsoft/WSL#41917](https://github.com/microsoft/WSL/issues/41917)). WSL then shows `Hyper-V: Nested features: 0x0` and has no `/dev/kvm`.
   - `host/waa-rollback.ps1` removes that update with DISM and pauses Windows Update for 7 days. It's a system change, so the PC's owner runs it, then reboots.
2. **The distro:** Ubuntu 24.04, imported as `waa`, with systemd. Then run in order:
   - `host/waa-prep.sh`: Docker, WAA cloned, patched and made executable, and the base image (44 GB);
   - `host/waa-iso.sh`: the Windows 11 Enterprise evaluation ISO, moved to `vm/image/setup.iso`;
   - `host/waa-build.sh`: the `winarena-dev` image.
3. **The patch** (`host/waa-patch.py`). WAA's VM setup no longer completes unpatched:
   - its LibreOffice 24.8.2 mirrors are 404;
   - the Caddy download from caddyserver.com hangs;
   - pywin32 312 can't import `win32ui` without the MFC runtime, so WAA's in-VM server dies at start.
4. **The golden image:** `host/waa-prepare.sh`, started through `host/waa-start.ps1`. That script launches it through WMI, so it survives SSH disconnects. It takes about 1.5 h, then copy `vm/storage` to `vm/storage-golden`.
5. **The run.**
   - Serve: `host/waa-start.ps1 -Script waa-serve.sh -Log /root/waa-serve.log` starts the container with the VM up and no stock agent.
   - Provision: `vm/oriel-provision.ps1` (OpenSSH server with key login, `oriel install --autostart`) and, for the comparison, `vm/cua-provision.ps1`, both through `host/vm-exec.sh`.
   - The layer's MCP server is then `host/vm-oriel-mcp.sh`: SSH to the container's address, which dockur forwards to the VM.
6. **From the client computer:** `ORIEL_BENCH_SSH=<the PC's ssh name> python3 run.py --layer oriel --out results-oriel.jsonl`.

Before WAA's own server is up, `host/qmp_keys.py` types into the VM through QEMU's monitor. That's how the stuck setup was diagnosed and fixed.

## Setup on a DigitalOcean droplet

The full runs used one Intel droplet with nested KVM (32 vCPUs), running 10 to 14 WAA instances side by side.

1. Copy `host/` and `waa_env.py` to `/root` on the droplet and run `do-setup.sh`: Docker, WAA cloned and patched, the images and the Windows ISO, then the golden image build in the background (log: `/root/waa-prepare.log`).
2. Provision the image once as above, and save it with `waa-snapshot.sh save <dir>`.
3. `do-instances.sh <first> <last> <disk dir>` makes and boots instances `waa<first>` to `waa<last>` from that disk, and `do-roles.sh <layer> <first> <last>` sets each VM up for its tool layer.
4. From the client computer: `WAA_DO_HOST=<droplet address> ./run-do.sh oriel:0-4 cua:5-9` runs both layers, each split across its instances. The SSH key comes from `WAA_DO_KEY` (default `~/.ssh/do_waa_ed25519`).

## Round 10 on firecracker

oriel 0.1.3 against Cua Driver 0.46.1, two runs per layer over the same tasks, published whichever side wins.

**oriel version:** the pilot ran oriel 0.1.1; the full run uses 0.1.3, the release of 12 Nov 2026 (the user's decision). 0.1.2 added MCP tool annotations to every tool; 0.1.3 is a security fix to `launch`, which now checks the program Windows would run for a path and refuses more programs ([CHANGELOG](../../CHANGELOG.md)). For it, `storage-provisioned` and `storage-warm` were made again on 12 Nov from the 11 Nov `storage-golden`, with the 0.1.3 release's `oriel.exe` (SHA-256 `35081509…83d9f7`, as in its `SHA256SUMS`); Cua Driver 0.46.1 (`ec49cca5…`) and everything else stay as in the pilot. Provisioning took 4 minutes, the warm-up 16. In both warm-up tries LibreOffice's first-run updater ran for about 8 minutes, then LibreOffice closed without opening Calc (on 11 Nov Calc opened after it). The first try then stopped; in the second, Calc was started once more by hand, and `do-warm.sh` went on (Calc, then Writer) and saved `storage-warm`. `do-warm.sh` now stops with the window list when an app never opens; before, it stopped without a message.

**The same as round 9:** Claude Sonnet 5.5, the prompt and the one-sentence hint per layer, 50 turns, 900 s per task, Cua Driver's 4 hidden tools, 10 VMs of 8 GB and 4 vCPUs (5 per layer), every VM rebuilt from the warmed image before each run. Cua Driver 0.46.1 has the same 63 tools as 0.38.0, and its docs describe the 4 hidden ones in the same words as 0.38.0's, so the round 9 rule stands.

**What changed:**
- **Host:** firecracker, the lab's Linux machine (Threadripper 9960X, 48 threads, 122 GiB, KVM on bare metal), instead of a droplet with nested KVM. The agents run there too (`run.py --remote local`), so no client connection can drop mid-run as in rounds 8 and 9.
- **Windows image:** built new on 11 Nov 2026 from the evaluation ISO that Microsoft's link served that day: Windows 11 Enterprise Evaluation 26H2, build 26300.9457, with WAA at commit eb220c4 and the apps WAA's setup installs (Chrome and VS Code at their latest releases). Round 9's build isn't recorded.
- **Agent settings:** Claude Code runs with its own settings folder (`host/agent-home.sh`): no CLAUDE.md, skills, plugins or hooks of the machine's user, and no auto-update during the run. Rounds 1 to 9 ran with the Mac user's settings. On 11 Nov a one-line prompt with round 9's flags started from 50,040 tokens under those settings, 12,682 in the agents' own folder, and 3,652 with round 10's flags.
- **Model and effort pinned:** `--model claude-sonnet-5-5 --effort xhigh`. Round 9 passed `sonnet`, which named Sonnet 5.5 then and still does, and took the effort from the user's settings. They say `xhigh` in every settings file and backup from 3 Nov on; no record shows round 9's own value. Effort `xhigh` for round 10, pilot and full run, is the user's decision of 11 Nov 2026.
- **Claude Code's own tools:** only ToolSearch, which loads deferred MCP tools (`--tools ToolSearch`). Round 9's list of tools to drop leaves 14 newer tools in Claude Code 2.1.322 (Workflow, SendMessage, Skill, Cron and others). The agents use the rig's Claude Code (2.1.317 on 11 Nov); `round10.sh` pins the build installed when it starts, for all runs.
- **Background load:** other work shares firecracker, also at night. Both layers of a run go at the same time on the same host (5 VMs each, as in round 9), so that load hits both alike. Each row now records the host's 1-minute load and the PSU's total output at the task's start and end (`host_load`, `host_psu_w`, from `host/host-load.sh`), and `stats.py` prints each run's mean.
- **Network:** each VM's web viewer and RDP are published on 127.0.0.1 only (`host/waa-patch.py`).
- **VM network on this host:** the WAA image's network script forces legacy iptables, whose kernel modules firecracker (Ubuntu 26.04, nftables) doesn't load, so the VM fell back to user-mode networking, where WAA can't reach its server. `host/waa-patch.py` keeps the image's iptables-nft instead; nothing on the host changes.

### Setup on firecracker

As the user `yordidekleijn` (in the docker group, no sudo), everything in `WAA_HOME=~/waa`:

```bash
export WAA_HOME=~/waa
git clone -b main https://github.com/YordiLorenzo/oriel.git $WAA_HOME/oriel
cp $WAA_HOME/oriel/bench/waa/host/* $WAA_HOME/oriel/bench/waa/waa_env.py $WAA_HOME/
bash $WAA_HOME/do-setup.sh                                      # WAA, patches, ISO, images; the golden image builds in the background
bash $WAA_HOME/oriel/bench/waa/host/waa-tools.sh 0.1.3 0.46.1  # oriel and Cua Driver into the share, hashes checked
bash $WAA_HOME/agent-home.sh                                    # the agents' Claude Code folder
```

Then, as in rounds 8 and 9, provision the golden image and warm it (LibreOffice's first start), once the golden build's container has exited: `bash $WAA_HOME/waa-provision.sh oriel-0.1.3.exe`. It saves `storage-golden`, installs OpenSSH with the VM key and oriel (`vm/oriel-provision.ps1`) and unpacks Cua Driver with its telemetry off (`vm/cua-provision.ps1`), saves `storage-provisioned`, then runs `do-warm.sh`, which saves `storage-warm`. For a new oriel build, run `waa-tools.sh` and `waa-provision.sh` again: when `storage-golden` exists, the provisioning starts from it.

On 11 Nov, with other work keeping the rig at a load of 50 to 110: the golden image took 40 minutes (18 GB on disk), provisioning 5 minutes, the warm-up 13 (LibreOffice's first-run updater alone 525 s). LibreOffice's first start also opens a web link, and Windows then shows "Pick an app"; those dialogs end at shutdown, and the clean slate before each task closes them anyway.

### Pilot (11 Nov 2026)

Two VMs from `storage-warm`, oriel 0.1.1 on waa0 and Cua Driver 0.46.1 on waa1, both at the same time, over 5 dev-half tasks that all four round 9 runs completed (Chrome, Edge, File Explorer, LibreOffice Calc, VS Code), one run each, effort `xhigh`. Rows in `pilot/results-pilot-r10-*.jsonl`.

| | oriel 0.1.1 | Cua Driver 0.46.1 |
|---|---|---|
| tasks completed | 5 of 5 | 5 of 5 |
| time per task, mean | 19.6 s | 32.8 s |
| turns per task, mean | 13.4 | 14.2 |
| model cost, all 5 | $0.29 | $0.81 |
| the same 5 tasks in round 9, mean time / cost per task | 27.5 s / $0.140 | 38.5 s / $0.244 |
| host load, 1 min, at the tasks' starts and ends | 1.9 to 2.8 | 1.9 to 2.8 |
| PSU output | 284 to 600 W | 276 to 514 W |

Both agents connected to their MCP server on every task. The agent saw 19 oriel tools or 59 Cua Driver tools, plus ToolSearch, and none of Cua's 4 hidden tools. Cua's agent called ToolSearch once per task, since its tools load on demand; oriel's load up front. No trace shows a Cua update notice. Background load was the same for both layers. WAA's reset and scoring added about 25 s per task. Five tasks are too few to compare the layers; the pilot only shows that setup, agents, scoring and result rows work.

### Smoke check of the 0.1.3 images (12 Nov 2026)

Two VMs from the new `storage-warm`, oriel 0.1.3 on waa0 and Cua Driver 0.46.1 on waa1, at the same time, one dev-half task from the pilot (File Explorer, `f934d80d…-WOS`), with the full run's settings. Rows in `pilot/smoke-r10-*.jsonl`.

| | oriel 0.1.3 | Cua Driver 0.46.1 |
|---|---|---|
| task completed | yes | yes |
| time / turns / model cost | 25.6 s / 9 / $0.05 | 34.8 s / 16 / $0.13 |
| the same task in the pilot (oriel 0.1.1) | 19.5 s / 16 / $0.07 | 42.0 s / 18 / $0.18 |
| tools the agent saw | 19 + ToolSearch | 59 + ToolSearch |
| host load, 1 min / PSU output | 4.0 to 4.8 / 136 to 162 W | 4.1 to 4.8 / 136 to 150 W |

Both MCP servers connected, both agents ran Claude Code 2.1.317 on `claude-sonnet-5-5`. Memory: each VM's QEMU process held 8.0 GiB and each agent 0.2 GiB, so the 10 VMs and 10 agents of the full run take about 83 GiB; the start check asks for 90.

### The full run

The run starts after 00:00 on Fri 13 Nov, once the start check below passes (heavy work on firecracker runs from 00:00 to 08:00). Expected: about 1 hour per run, 2.5 hours for both with the VM rebuilds. Each run is bounded by Cua Driver's side: 3.3 agent hours at the pilot's pace (3.8 at round 9's), plus about 25 s of reset and scoring per task, over 5 VMs. Model cost, as Claude Code estimates it: about $110 for all four runs at the pilot's rates ($200 at round 9's).

Power: firecracker switched off twice on 11 Nov at 900 to 950 W from other sessions' GPU and CPU jobs, and the round 10 setup was paused for it that evening. So the run starts only after a check (the user's rule of 11 Nov): `waa-round10.timer`, a systemd user timer on firecracker, starts `waa-round10.service` at 00:00, which runs `host/round10-gate.py`. That starts `round10.sh` only when, at one check:
- every reading of Home Assistant's `sensor.firecracker_power` was under 700 W for 15 minutes in a row. The readings come from the MQTT topic that sensor reads (`homelab/power/firecracker/state`, one every 30 s from the rig's power publisher), with the publisher's own login, since Home Assistant's token isn't on the rig. A minute without a reading starts the count again;
- at least 90 GiB of memory is free (the 10 VMs and their agents take about 83 GiB, from the smoke check);
- nothing is in the way: no WAA container, no other round 10 run, and 200 GB of free disk.

It checks once a minute, so the earliest start is 00:15. If the checks don't all pass by 03:00, the run doesn't start, and the log says why. The 10 VMs add load of their own once it starts.

1. Install the timer, after copying `host/` to `~/waa` as in the setup:
   ```bash
   cp ~/waa/oriel/bench/waa/host/waa-round10.service ~/waa/oriel/bench/waa/host/waa-round10.timer ~/.config/systemd/user/
   rm -f ~/waa/round10.ended; systemctl --user daemon-reload; systemctl --user enable --now waa-round10.timer
   ```
   `python3 ~/waa/round10-gate.py --dry-run` runs the same checks by hand and never starts the run. Tested on 12 Nov with 2 minutes for 15: with a 5 W limit it logged "not started" after 3 checks and exited 1; with 700 W it logged "all clear" after 2 checks (4 readings, 166 to 174 W) and exited 0. Installed on 12 Nov: `systemctl --user list-timers` shows the next start at Fri 2026-11-13 00:00:00 CET.
2. No nap until the run ends: a user timer can't wake the rig, and an open ssh session keeps it awake (firecracker skill, `nap-and-wake.md`). So one ssh session waits until the service has stopped (it then touches `~/waa/round10.ended`), for 30 hours at most, held from a machine that stays on:
   ```bash
   nohup ssh -o BatchMode=yes -o ServerAliveInterval=60 firecracker 'timeout 30h sh -c "until [ -e ~/waa/round10.ended ]; do sleep 60; done"' > awake-hold.log 2>&1 < /dev/null &
   ```
   `nap status` on the rig then shows "1 ssh session(s)". The session ends when the run ends, and also when the rig reboots or the holding machine sleeps.
3. `round10.sh` does each run in turn: rebuild the 10 VMs from `storage-warm`, set up waa0-4 for oriel and waa5-9 for Cua Driver, run both layers (each split over its 5 VMs), then a second pass for tasks that got no row. Its last line is `round 10 done`.
4. Watch it from the Mac: `ssh firecracker 'bash ~/waa/round10-status.sh'` shows the timer, the service's state and result (`inactive`, `success` once both runs ended), rows per results file (153 is a full run), the VMs up and the log's last lines. The log is `~/waa/round10.log` on firecracker: one line per check, then `round10.sh`'s output. Each VM's log is `logs/<tag>-waa<i>.log`, each task's agent trace `traces/<tag>/<task>.jsonl`.
5. Results land on the rig in `~/waa/oriel/bench/waa`: `results-oriel-0.1.3-t1.jsonl`, `-t2`, `results-cua-r10-t1.jsonl`, `-t2`. Copy them into this folder, add round 10 to `ROUNDS` in `report.py`, and run `stats.py --oriel results-oriel-0.1.3-t1.jsonl results-oriel-0.1.3-t2.jsonl --cua results-cua-r10-t1.jsonl results-cua-r10-t2.jsonl`. Its last table gives each run's mean host load and PSU output; say in RESULTS.md if the layers' load differed.
6. Afterwards, free the VMs and their disks (each `~/waa/waa-<i>` holds 18 GB, partly root's): `ssh firecracker 'docker rm -f $(docker ps -aq --filter name=waa); for i in $(seq 0 9); do WAA_HOME=~/waa ~/waa/as-root.sh rm -rf ~/waa/waa-$i; done'`.

## Not tuning to the benchmark

1. **Only general fixes.** A change to oriel must fix something a user on their own PC would hit, and the commit says which failure motivated it. Nothing may depend on WAA's tasks, files or evaluators.
2. **Held-out half.** `tasks.json` splits the tasks by a hash of their id into `dev` (85) and `holdout` (69). After the first oriel run, only dev traces are read when looking for failures. Every result is reported for both halves; a fix that helps only dev is overfitting.
3. **Same conditions for every layer.** Same model, prompt, turn limit and patched environment. A prompt change means rerunning every layer.
4. **No regressions elsewhere.** A new oriel build also runs the 18-task suite in `bench/tasks` before it counts.
5. **The first run stays the headline baseline.** Later versions are reported next to it, never instead of it.

The environment patches in `host/waa-patch.py` are a different kind of change: they repair WAA's setup, which no longer works as published, the same way for every layer.

## Environment notes per round

- **Round 4** added a clean slate before each task (LibreOffice and "Pick an app" dialogs closed).
- **Round 5** also clears LibreOffice's recovery list. About 20 minutes into run 1, Windows Update, its orchestrator, search indexing and SysMain were stopped in all 14 VMs alike: freshly restored VMs were running them at 200-250% CPU each, which made every accessibility call several times slower. Defender was left as it is (it blocks scripts that change it, and the benchmark doesn't work around that).
- **Round 6** stops those services at boot in every role.
- **Round 7** runs 10 VMs instead of 14 (14 kept the droplet at load 32-48 on 32 vCPUs). Run 1 (oriel v9, Cua) is clean. Run 2 (oriel v10, Cua) is not a fair measurement:
  - its first attempt hit the Claude account's weekly limit, and those rows were dropped and rerun;
  - the rerun started after the VMs had sat idle for about 20 hours;
  - a Windows Firewall prompt raised by a VS Code task ("allow Visual Studio Code to access networks?") stayed open through every later task on its VM;
  - a notification toast kept the foreground on others;
  - oriel refuses keys while another window holds the foreground, and that blocked 38 of 72 dev tasks;
  - Cua typed on regardless.
- **Round 8** rebuilds every VM from the warmed image before the run. The clean slate also closes a leftover firewall prompt, which is Cancel: nothing is allowed through the firewall.
  - Run 2 was rerun from the start: the Mac running the agents lost its connection partway, so about 20 rows were scored against a dead connection (set aside, not in the results). Since then `run.py` writes no row when the MCP server doesn't connect or scoring fails, so a resumed run retries the task.
- **Round 9** is round 8 with oriel v12. The account's usage limit cut one Cua task short in run 1 (its row was set aside), and the Mac lost its connection to the VMs for a few minutes in run 2 (those tasks got no row). All of them were rerun. Since then `run.py` also writes no row when the reply is the usage-limit message.

## Results

`RESULTS.md` has every round, per app and per half (`report.py` writes it from the `results-*.jsonl` files). The summary is in [docs/benchmarks.md](../../docs/benchmarks.md#3-windowsagentarena-benchwaa). `pilot/` holds the first, partial runs, which aren't in `RESULTS.md`:
- `results-oriel-v1.jsonl`: a 20-task pilot with the first prompt and a 30-turn limit, which led to the current prompt and the 50-turn limit;
- `results-oriel-v2.jsonl`: a run in which agents typed PowerShell into an open terminal once `launch` refused to start one, which is why open terminals are off limits by default;
- `results-oriel-v3-partial.jsonl`: 7 tasks with oriel v3, stopped early; `smoke*.jsonl`: short checks of the harness;
- `wsl-partial/`: partial runs on the Windows PC before the move to the droplet.
