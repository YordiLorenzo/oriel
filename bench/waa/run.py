#!/usr/bin/env python3
"""WindowsAgentArena with a swapped tool layer: WAA sets up and scores each task with its own code (waa_env.py in the
WAA container), and the agent (Claude Code headless) does the task through one computer-use MCP server running in the
WAA Windows VM, with no other tools.

    python3 run.py [--layer oriel] [--remote wsl|do] [--container winarena] [--shard 0/12] [--domains chrome]
                   [--only <task_id>,...] [--out FILE]

The WAA host is a Windows PC with the WSL distro "waa" (--remote wsl, the PC's ssh name in $ORIEL_BENCH_SSH) or a
DigitalOcean droplet (--remote do, address in $WAA_DO_HOST, key in $WAA_DO_KEY, default ~/.ssh/do_waa_ed25519). On it,
the container given with --container runs with its VM up, and /root/vm-<layer>-mcp.sh reaches the layer's MCP server
in that VM. See README.md here. --shard i/N takes every N-th task, so N copies on N containers split a run; they can
share one output file. Rows already in the output file are skipped, so an interrupted run resumes.
"""

import argparse
import json
import os
import subprocess
import tempfile
import time

REMOTES = {
    "wsl": ["ssh", os.environ.get("ORIEL_BENCH_SSH", ""), "wsl", "-d", "waa", "-u", "root", "--"],
    "do": ["ssh", "-i", os.path.expanduser(os.environ.get("WAA_DO_KEY", "~/.ssh/do_waa_ed25519")), "-o", "IdentityAgent=none",
           "-o", "IdentitiesOnly=yes", "-o", "ServerAliveInterval=30", "root@" + os.environ.get("WAA_DO_HOST", "")],
}
REMOTE = REMOTES["wsl"]
CLIENT = "/root/WindowsAgentArena/src/win-arena-container/client"

ORIEL_HINT = "The oriel tools read windows as text (tree, find) and act on elements by ref (invoke, set, select, scroll, keys)."

# Each layer's MCP server runs in the VM of the container given with --container. "oriel-shell" is the same server
# with {"shell": true} in the VM's oriel config.json; which VM has that is set up on the VM side (README).
LAYERS = {
    "oriel": {"server": "oriel", "mcp": "/root/vm-oriel-mcp.sh", "allowed": ["mcp__oriel"], "hint": ORIEL_HINT},
    "oriel-shell": {"server": "oriel", "mcp": "/root/vm-oriel-mcp.sh", "allowed": ["mcp__oriel"], "hint": ORIEL_HINT},
    # Cua Driver 0.38.0 as shipped (63 tools, its browser tools included) except run_script (scripting is outside the
    # as-shipped oriel comparison; the oriel-shell run covers agents with a shell) and the tools that install or
    # update software.
    "cua": {"server": "cua", "mcp": "/root/vm-cua-mcp.sh", "allowed": ["mcp__cua"],
            "hidden": ["mcp__cua__" + t for t in ("run_script", "install_ffmpeg", "install_extension", "check_for_update")],
            "hint": "Cua Driver reads a window with get_window_state (include_screenshot false keeps it fast) and acts on "
                    "element tokens (click, set_value), with keyboard and mouse tools too."},
}

# v2 after a 20-task pilot (pilot/): agents gave up while LibreOffice was still opening, and stopped to ask for
# confirmation on what is a dedicated test VM.
PROMPT = """You control a Windows 11 PC through the MCP tools you have (no other tools). {hint}
This PC is a dedicated test machine: work on your own, never ask for confirmation, and open, close or restart apps \
as the task needs. The task's files and apps have already been set up for you. An app may take a few seconds to \
finish opening, so check the open windows (and wait if needed) before concluding that something is missing.

Task: {instruction}

When you are done, end your reply with DONE. If the task truly can't be done on this PC, end with INFEASIBLE instead."""


def waa(container: str, *args: str, timeout: int = 600) -> dict:
    """Run waa_env.py in the WAA container; return its last JSON line."""
    p = subprocess.run([*REMOTE, "docker", "exec", "-w", "/client", container, "python", "waa_env.py", *args],
                       capture_output=True, text=True, timeout=timeout)
    for line in reversed(p.stdout.splitlines()):
        if line.startswith("{"):
            return json.loads(line)
    raise RuntimeError(f"waa_env.py {' '.join(args)}: {(p.stdout + p.stderr)[-600:]}")


def run_agent(prompt: str, mcp: dict, layer: dict, model: str, max_turns: int, timeout: int, trace: str) -> dict:
    """Run the agent; its full event stream goes to `trace`, and the final result event is returned."""
    with tempfile.TemporaryDirectory() as empty:
        cmd = ["claude", "-p", prompt, "--model", model, "--output-format", "stream-json", "--verbose", "--max-turns", str(max_turns),
               "--mcp-config", json.dumps({"mcpServers": {layer["server"]: mcp}}),
               "--strict-mcp-config", "--allowedTools", *layer["allowed"],
               "--disallowedTools", "Bash Edit Write Read Glob Grep WebFetch WebSearch Task NotebookEdit TodoWrite",
               *layer.get("hidden", [])]
        start = time.time()
        with open(trace, "w") as f:
            try:
                p = subprocess.run(cmd, cwd=empty, stdout=f, stderr=subprocess.PIPE, text=True, timeout=timeout)
                err = p.stderr
            except subprocess.TimeoutExpired:
                err = "TIMEOUT"
        out = {"result": err[-500:], "is_error": True}
        for line in open(trace):
            event = json.loads(line) if line.startswith("{") else {}
            if event.get("type") == "result":
                out = event
        out["wall_s"] = round(time.time() - start, 1)
        return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--layer", default="oriel", choices=list(LAYERS))
    ap.add_argument("--model", default="sonnet")
    ap.add_argument("--meta", default="test_all.json", help="task list in evaluation_examples_windows")
    ap.add_argument("--domains")
    ap.add_argument("--only")
    ap.add_argument("--max-turns", type=int, default=50)
    ap.add_argument("--timeout", type=int, default=900)
    ap.add_argument("--container", default="winarena", help="the WAA container whose VM runs the tasks")
    ap.add_argument("--remote", default="wsl", choices=list(REMOTES))
    ap.add_argument("--shard", default="0/1", help="i/N: run every N-th task, starting at i")
    ap.add_argument("--tag", help="name of this run for its traces and default output file (default: the layer)")
    ap.add_argument("--out")
    args = ap.parse_args()
    tag = args.tag or args.layer
    out_path = args.out or f"results-{tag}.jsonl"
    layer = LAYERS[args.layer]
    global REMOTE
    REMOTE = REMOTES[args.remote]
    shard, shards = map(int, args.shard.split("/"))
    os.makedirs(f"traces/{tag}", exist_ok=True)

    meta = json.loads(subprocess.run([*REMOTE, "cat", f"{CLIENT}/evaluation_examples_windows/{args.meta}"],
                                     capture_output=True, text=True, check=True).stdout)
    tasks = [(d, t) for d, ids in meta.items() for t in ids
             if (not args.domains or d in args.domains.split(",")) and (not args.only or t in args.only.split(","))]
    tasks = tasks[shard::shards]
    try:
        done = {json.loads(line)["task"] for line in open(out_path)}
    except FileNotFoundError:
        done = set()

    for domain, task_id in tasks:
        if task_id in done:
            continue
        try:
            instruction = waa(args.container, "reset", domain, task_id)["instruction"]
        except Exception as e:
            print(f"{domain}/{task_id} SETUP ERROR {e}", flush=True)
            continue
        trace = f"traces/{tag}/{task_id}.jsonl"
        mcp = {"command": REMOTE[0], "args": [*REMOTE[1:], layer["mcp"], args.container]}
        out = run_agent(PROMPT.format(hint=layer["hint"], instruction=instruction), mcp, layer, args.model,
                        args.max_turns, args.timeout, trace)
        # The Mac's own connection dropping is no result for either layer: no row, so the next run retries the task.
        if '"name":"%s","status":"failed"' % layer["server"] in open(trace).read():
            print(f"{domain}/{task_id} MCP ERROR the server didn't connect", flush=True)
            continue
        result = out.get("result") or ""
        # So is the account's usage limit: the model never ran.
        if out.get("is_error") and result.startswith("You've hit your"):
            print(f"{domain}/{task_id} LIMIT {result}", flush=True)
            continue
        infeasible = result.rstrip().rstrip(".").endswith("INFEASIBLE")
        try:
            score = waa(args.container, "eval", domain, task_id, *(["--fail"] if infeasible else []))["score"]
        except Exception as e:
            print(f"{domain}/{task_id} EVAL ERROR {e}", flush=True)
            continue
        row = {"task": task_id, "domain": domain, "layer": args.layer, "model": args.model, "score": score,
               "infeasible_claimed": infeasible, "turns": out.get("num_turns"), "wall_s": out.get("wall_s"),
               "cost_usd": out.get("total_cost_usd"), "error": out.get("is_error"), "subtype": out.get("subtype"),
               "instruction": instruction, "result": result[-400:]}
        print(f"{domain}/{task_id[:8]} score={score} turns={row['turns']} wall={row['wall_s']}s ${row['cost_usd'] or 0:.2f}", flush=True)
        with open(out_path, "a") as f:
            f.write(json.dumps(row) + "\n")


if __name__ == "__main__":
    main()
