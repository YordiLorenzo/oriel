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
| `run.py` | Runs on the client computer (a Mac in these runs). For each task: WAA reset, agent run, WAA evaluate, then one JSON row. Resumes from its output file. |
| `waa_env.py` | Runs in the WAA container (`/client`). Calls WAA's setup and scoring. |
| `host/` | Scripts for the WAA host: the WSL2 distro `waa` on a Windows PC (`waa-*`), or a DigitalOcean droplet (`do-*`). |
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
