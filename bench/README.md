# Benchmarks

The scripts behind the numbers in [docs/benchmarks.md](../docs/benchmarks.md). Each one drives a Windows PC from another
computer (a Mac in the published runs) over SSH.

| Path | What it measures | Results |
|---|---|---|
| `latency.py` | oriel's daemon, action by action | [docs/benchmarks.md](../docs/benchmarks.md#1-latency-per-call-benchlatencypy) |
| `headtohead/` | the same UI tasks through oriel, Cua Driver, Terminator and Windows-MCP, timed per call | [headtohead/RESULTS.md](headtohead/RESULTS.md) |
| `tasks/` | an 18-task agent suite: Claude Code with one tool layer, scored by an independent check | [docs/benchmarks.md](../docs/benchmarks.md#4-agent-task-suite-benchtasks) |
| `waa/` | WindowsAgentArena, Microsoft's 154-task benchmark, with only the agent's tools swapped | [waa/README.md](waa/README.md), [waa/RESULTS.md](waa/RESULTS.md) |

## What you need

- A Windows PC with OpenSSH Server and key login, so `ssh <name>` works from the client computer.
- On the client: Python 3.11 or newer; Claude Code (`claude`) for `tasks/` and `waa/`; [uv](https://docs.astral.sh/uv/)
  and `nc` for `headtohead/`.
- A PC nobody uses while a run is going. The scripts only touch windows they open themselves, but the agent runs are
  real model calls on a real desktop.

## Settings

The client-side scripts read these environment variables:

| Variable | Used by | What |
|---|---|---|
| `ORIEL_BENCH_SSH` | `tasks/`, `headtohead/`, `waa/` (`--remote wsl`) | the Windows PC's ssh name (required) |
| `ORIEL_BENCH_DIR` | `tasks/`, `headtohead/` | the benchmark folder on the PC (default `C:\oriel-bench`) |
| `WINDOWS_MCP_TOKEN` | `headtohead/` | a bearer token, if your Windows-MCP server asks for one |
| `WAA_DO_HOST`, `WAA_DO_KEY` | `waa/` (`--remote do`) | the droplet's address, and its SSH key (default `~/.ssh/do_waa_ed25519`) |

The PowerShell scripts that run on the PC take `-Dir`, which defaults to `$env:ORIEL_BENCH_DIR` on the PC, else
`C:\oriel-bench`. The benchmark folder holds:

```
oriel\oriel.exe          the build under test (tasks\oriel-deploy.ps1 puts a fresh one here)
oriel-tasks\site\        a copy of bench\tasks\site
oriel-tasks\sandbox\     made by the task suite, one folder per File Explorer task
cua-driver\, terminator\ filled by headtohead\get-competitors.ps1
downloads\
```

If oriel is also installed on the PC, its daemon answers on the same per-user port as the build under test. Stop it
first (`oriel uninstall`), so the run measures the build in the benchmark folder.

## Agent task suite (`tasks/`)

Before the first run:

1. Build oriel (see the main README) and run `bench\tasks\oriel-deploy.ps1` on the PC, or copy `oriel.exe` to
   `<dir>\oriel\` yourself.
2. Copy `bench/tasks/site` to `<dir>\oriel-tasks\site`.
3. Start a Chrome for the agents with its own profile and DevTools on loopback, for example
   `chrome.exe --user-data-dir=<dir>\chrome-profile --remote-debugging-port=9222`. The suite opens and closes its test
   page through that port. Chrome 136 and later ignore that switch on the default profile.
4. Set `PC_BIOS`, `PC_BUILD` and `PC_MEMORY` at the top of `tasks/run.py` to what System Information shows on your PC:
   tasks M1 to M3 check the agent's answers against them.
5. For the `cua` and `terminator` layers, also set up the competitors as in the head-to-head below.

Then, from the client:

```sh
export ORIEL_BENCH_SSH=my-windows-pc ORIEL_BENCH_DIR='C:\oriel-bench'
python3 bench/tasks/run.py --layer oriel --trials 2 --out results.jsonl
```

The suite touches only Character Map, System Information, File Explorer windows on its sandbox folder and the agents'
Chrome. Task C4 turns Character Map's advanced view back off afterwards (a setting under HKCU). It needs no keyboard
input, so it also runs while the PC is locked. Each task is one headless Claude Code run with only the layer's MCP
tools; with Sonnet, an oriel task cost about $0.12.

## Head-to-head (`headtohead/`)

1. On the PC, run `get-competitors.ps1`: it downloads Cua Driver 0.38.0 and Terminator MCP 0.24.28 from their official
   sources and checks their hashes. Then, in a PowerShell window on the PC's desktop (an SSH session has no desktop),
   run `bench-start.ps1` and `relay-start.ps1`. For Windows-MCP 0.8.8, start its Streamable HTTP server on loopback port
   8765, as its own documentation describes.
2. On the client, open the tunnels:
   `ssh -N -L 23017:127.0.0.1:3017 -L 23018:127.0.0.1:3018 -L 28765:127.0.0.1:8765 $ORIEL_BENCH_SSH`.
3. Leave a Chrome window and an Edge window open on the PC (their trees are read, nothing is changed).
4. `uv run --script bench/headtohead/headtohead.py --runs 5`.

Pass `--unlocked` only on an unlocked PC that nobody is using: Windows-MCP's click and type send real mouse and
keyboard input.

## Latency (`latency.py`)

After `oriel install` on the PC: `python3 bench/latency.py --host my-windows-pc`. The script's header lists the
scenarios and options.

## WindowsAgentArena (`waa/`)

See [waa/README.md](waa/README.md): it needs a host with nested virtualization (a Windows PC with WSL2, or a cloud VM)
and tens of gigabytes of images and VM disks. A full run of one layer cost $38 to $61 in model usage in round 9.
