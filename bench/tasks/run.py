#!/usr/bin/env python3
"""Agent task suite: an LLM agent (Claude Code headless) does Windows tasks through one computer-use MCP server, and an
independent checker scores each task (file system, WMI, or a separate read of the window).

    python3 run.py [--layer oriel] [--model sonnet] [--only W1,E2] [--trials 1] [--out results.jsonl]

Everything runs on one Windows PC, reached over SSH, on things the suite owns: a local test site in the agents' own
Chrome (DevTools on 127.0.0.1:9222 there), Character Map, File Explorer on a sandbox folder, and System Information
(read only). It never needs keyboard input, so it runs on a locked PC; Store apps are suspended there and left out.
The agent gets only the layer's MCP tools (no shell, no files) and works in an empty directory.

    ORIEL_BENCH_SSH   the Windows PC's ssh name (required)
    ORIEL_BENCH_DIR   the benchmark folder on that PC (default C:\\oriel-bench): oriel\\oriel.exe, oriel-tasks\\site
                      (a copy of site/ here) and oriel-tasks\\sandbox; see bench/README.md
"""

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import time

HOST = os.environ.get("ORIEL_BENCH_SSH", "")
BENCH = os.environ.get("ORIEL_BENCH_DIR", r"C:\oriel-bench").rstrip("\\")
ORIEL = BENCH + r"\oriel\oriel.exe"
SANDBOX = BENCH + r"\oriel-tasks\sandbox"
SITE = "file:///" + BENCH.replace("\\", "/") + "/oriel-tasks/site/form.html"

# What System Information shows on the PC the suite was written on (M1-M3). Change these for yours.
PC_BIOS = "2611"
PC_BUILD = "26200"
PC_MEMORY = r"31[.,]2|32[.,]0|32 ?GB"

# Each layer: its MCP server as Claude Code launches it, the tools the agent may use, and a one-line hint of similar
# length. Competitors get only their accessibility tools: on a locked PC a keyboard or mouse tool aimed at the
# foreground would type into the lock screen, and their shell, file and script tools are outside a fair comparison.
# They need their servers running on the PC (bench/headtohead/bench-start.ps1 and relay-start.ps1).
LAYERS = {
    "oriel": {
        "name": "oriel",
        "mcp": {"command": "ssh", "args": [HOST, ORIEL, "mcp"]},
        "allowed": ["mcp__oriel"],
        "hint": "The oriel tools read windows as text (tree, find) and act on elements by ref (invoke, set, select, scroll).",
    },
    "cua": {
        "name": "cua",
        # Cua Driver's daemon refuses SSH-session clients, so its stdio MCP runs in the desktop session behind a
        # loopback relay (port 3018); ssh -W carries stdio to it.
        "mcp": {"command": "ssh", "args": ["-W", "127.0.0.1:3018", HOST]},
        "allowed": ["mcp__cua__" + t for t in ("list_apps", "list_windows", "get_window_state", "click", "set_value",
                                                 "scroll", "launch_app", "get_accessibility_tree")],
        "hidden": ["mcp__cua__" + t for t in ("press_key", "hotkey", "type_text", "run_steps", "run_script", "kill_app",
                                                "clipboard_write", "drag", "move_cursor")],
        "hint": "Cua Driver reads a window with get_window_state (include_screenshot false keeps it fast) and acts on element tokens (click, set_value).",
    },
    "terminator": {
        "name": "terminator",
        "mcp": {"type": "http", "url": "http://127.0.0.1:23017/mcp"},
        "tunnel": ["-L", "127.0.0.1:23017:127.0.0.1:3017"],
        "allowed": ["mcp__terminator__" + t for t in ("get_applications_and_windows_list", "get_window_tree", "click_element",
                                                        "invoke_element", "set_value", "select_option", "set_selected",
                                                        "scroll_element", "open_application", "wait_for_element", "validate_element")],
        "hidden": ["mcp__terminator__" + t for t in ("press_key", "press_key_global", "type_into_element", "run_command",
                                                       "execute_sequence", "execute_browser_script", "mouse_drag", "write_file",
                                                       "edit_file", "copy_content", "gemini_computer_use")],
        "hint": "Terminator reads a window with get_window_tree and acts on elements by selector, e.g. 'role:Button && name:OK'.",
    },
}

PROMPT = """You control a Windows PC through the MCP tools you have (no other tools). The PC is locked, so keyboard \
input is unavailable: act on elements directly instead of typing keys. {hint}
Only touch the windows this task names; never touch, close or change any other window.

Task: {task}

When you are done, end your reply with a line "ANSWER: <answer>" if the task asks for information, otherwise "DONE". \
If the task can't be done, say why and end with "DONE"."""


def ssh(command: str, timeout: int = 60) -> str:
    return subprocess.run(["ssh", HOST, command], capture_output=True, text=True, timeout=timeout).stdout


def ps(script: str, timeout: int = 60) -> str:
    """Run PowerShell statements on the PC (one statement per line, passed on stdin)."""
    return subprocess.run(["ssh", HOST, "powershell -NoProfile -Command -"], input=script, capture_output=True,
                          text=True, timeout=timeout).stdout


def oriel(*args: str) -> str:
    """The referee: one oriel action through the daemon, outside the agent's session."""
    quoted = " ".join(f'"{a}"' if " " in a else a for a in args)
    return ssh(f"{ORIEL} call {quoted} 2>&1")


# --- setup and teardown helpers ---

def fresh_form():
    ps("$t = (Invoke-WebRequest -UseBasicParsing http://127.0.0.1:9222/json/list).Content | ConvertFrom-Json\n"
       "$t | ? { $_.url -like '*oriel-tasks/site*' } | % { Invoke-WebRequest -UseBasicParsing \"http://127.0.0.1:9222/json/close/$($_.id)\" | Out-Null }\n"
       f"Invoke-WebRequest -UseBasicParsing -Method Put 'http://127.0.0.1:9222/json/new?{SITE}' | Out-Null\n")
    time.sleep(1.5)


def close_charmap():
    ssh("taskkill /im charmap.exe /f >NUL 2>&1")


def close_msinfo():
    ssh("taskkill /im msinfo32.exe /f >NUL 2>&1")


def sandbox(name: str, files: dict[str, str]):
    """A fresh sandbox folder with the given files (relative path -> content)."""
    lines = [f"$d = '{SANDBOX}\\{name}'", "if (Test-Path $d) { Remove-Item -Recurse -Force $d }", "New-Item -ItemType Directory -Force $d | Out-Null"]
    for rel, content in files.items():
        lines.append(f"New-Item -ItemType Directory -Force (Split-Path '{SANDBOX}\\{name}\\{rel}') | Out-Null")
        lines.append(f"Set-Content -NoNewline -Path '{SANDBOX}\\{name}\\{rel}' -Value '{content}'")
    ps("\n".join(lines) + "\n")


def close_explorer(name: str):
    oriel("close", "--Window", name)


def exists(path: str) -> bool:
    return ps(f"Test-Path '{path}'\n").strip() == "True"


def answer(result: str) -> str:
    m = re.findall(r"ANSWER:\s*(.+)", result or "")
    return m[-1].strip() if m else ""


def status_text() -> str:
    out = oriel("find", "--Window", "Oriel test form", "--Name", "Order placed")
    m = re.search(r'Text "(Order placed[^"]*)"', out)
    return m.group(1) if m else out[-200:]


# --- the tasks: id -> (prompt, setup, check(result) -> (passed, detail), teardown) ---

TASKS = {
    "W1": ("In the Chrome window titled 'Oriel test form', fill in the order form for Grace Hopper (email grace@example.org): "
           "size Medium, express delivery, quantity 4. Then place the order.",
           fresh_form,
           lambda r: (lambda s: (s == "Order placed: Grace Hopper, Medium, express, quantity 4", s))(status_text()),
           lambda: None),
    "W2": ("In the Chrome window titled 'Oriel test form', place an order for Alan Turing: size Extra large, gift wrap, "
           "standard delivery, quantity 7, with the note 'leave at door'.",
           fresh_form,
           lambda r: (lambda s: (s == "Order placed: Alan Turing, Extra large, standard, gift wrap, quantity 7", s))(status_text()),
           lambda: None),
    "W3": ("In the Chrome window titled 'Oriel test form', the page has a numbered catalogue. What is the exact text of "
           "item number 87?",
           fresh_form, lambda r: ("Catalogue item 87" in answer(r), answer(r)), lambda: None),
    "W4": ("In the Chrome window titled 'Oriel test form', follow the link to page two and tell me the sentence that "
           "page shows under its heading.",
           fresh_form, lambda r: ("You made it to the second page" in answer(r), answer(r)), lambda: None),
    "W5": ("In the Chrome window titled 'Oriel test form', type 'Ada' as the full name, then clear the whole form with "
           "the form's own clear button. What does the status line under the form say now?",
           fresh_form, lambda r: ("Cleared" in answer(r), answer(r)), lambda: None),
    "W6": ("In the Chrome window titled 'Oriel test form', set the quantity slider to its maximum value and tell me that "
           "value.",
           fresh_form, lambda r: (answer(r).strip().rstrip(".") == "10", answer(r)), lambda: None),
    "C1": ("Open Character Map (charmap.exe). Put the text 'hello' in its 'Characters to copy' box, then press its "
           "Select button once. Leave the window open.",
           close_charmap,
           lambda r: (lambda v: ("hello" in v and len(v.strip()) == 6, v.strip()))(
               oriel("text", "--Window", "Character Map", "--Name", "Characters to copy", "--Type", "Edit").split("\n(")[0]),
           close_charmap),
    "C2": ("Open Character Map (charmap.exe) and tell me which font is selected in its Font box.",
           close_charmap, lambda r: ("arial" in answer(r).lower(), answer(r)), close_charmap),
    "C3": ("Open Character Map (charmap.exe). Is its 'Advanced view' check box ticked? Answer yes or no, and don't change it.",
           close_charmap, lambda r: (answer(r).lower().startswith("no"), answer(r)), close_charmap),
    "E1": (f"Open the folder {SANDBOX}\\e1 in File Explorer and rename the file draft.txt (Explorer may show it as "
           "'draft') to final.txt.",
           lambda: sandbox("e1", {"draft.txt": "draft"}),
           lambda r: (exists(f"{SANDBOX}\\e1\\final.txt") and not exists(f"{SANDBOX}\\e1\\draft.txt"), ""),
           lambda: close_explorer("e1")),
    "E2": (f"Open the folder {SANDBOX}\\e2 in File Explorer. How many PNG images are in it? Answer with a number.",
           lambda: sandbox("e2", {**{f"pic{i}.png": "x" for i in range(1, 8)}, "notes.txt": "x", "data.csv": "x"}),
           lambda r: (re.sub(r"\D", "", answer(r)) == "7", answer(r)),
           lambda: close_explorer("e2")),
    "E3": (f"Open the folder {SANDBOX}\\e3 in File Explorer and delete the file junk.tmp (it goes to the Recycle Bin). "
           "Leave report.txt alone.",
           lambda: sandbox("e3", {"junk.tmp": "x", "report.txt": "keep"}),
           lambda r: (not exists(f"{SANDBOX}\\e3\\junk.tmp") and exists(f"{SANDBOX}\\e3\\report.txt"), ""),
           lambda: close_explorer("e3")),
    "W7": ("In the Chrome window titled 'Oriel test form', which delivery option is currently selected? Don't change it.",
           fresh_form, lambda r: ("standard" in answer(r).lower(), answer(r)), lambda: None),
    "C4": ("Open Character Map (charmap.exe), find the Greek capital letter omega (Ω) using its search (it is in the "
           "advanced view), and get it into the 'Characters to copy' box. Turn the advanced view off again at the end.",
           close_charmap,
           lambda r: (lambda v: ("\u03a9" in v or "\u2126" in v, v.strip()))(
               oriel("text", "--Window", "Character Map", "--Name", "Characters to copy", "--Type", "Edit").split("\n(")[0]),
           lambda: (close_charmap(), ps("Set-ItemProperty HKCU:\\Software\\Microsoft\\CharMap -Name Advanced -Value 0\n"))),
    "E4": (f"Open the folder {SANDBOX}\\e4 in File Explorer and create a new, empty folder named Reports in it.",
           lambda: sandbox("e4", {"readme.txt": "x"}),
           lambda r: (exists(f"{SANDBOX}\\e4\\Reports"), ""),
           lambda: close_explorer("e4")),
    "M3": ("Open System Information (msinfo32.exe) and tell me the Windows build number it reports (just the number). "
           "Don't change anything.",
           close_msinfo, lambda r: (PC_BUILD in answer(r), answer(r)), close_msinfo),
    "M1": ("Open System Information (msinfo32.exe) and tell me the BIOS version it reports. Don't change anything.",
           close_msinfo, lambda r: (PC_BIOS in answer(r), answer(r)), close_msinfo),
    "M2": ("Open System Information (msinfo32.exe) and tell me how much total physical memory this PC has, as shown "
           "there. Don't change anything.",
           close_msinfo, lambda r: (bool(re.search(PC_MEMORY, answer(r))), answer(r)), close_msinfo),
}


def run_agent(prompt: str, layer: dict, model: str, timeout: int) -> dict:
    with tempfile.TemporaryDirectory() as empty:
        cmd = ["claude", "-p", prompt, "--model", model, "--output-format", "json",
               "--mcp-config", json.dumps({"mcpServers": {layer["name"]: layer["mcp"]}}),
               "--strict-mcp-config", "--allowedTools", *layer["allowed"],
               "--disallowedTools", "Bash Edit Write Read Glob Grep WebFetch WebSearch Task NotebookEdit TodoWrite", *layer.get("hidden", [])]
        start = time.time()
        try:
            p = subprocess.run(cmd, cwd=empty, capture_output=True, text=True, timeout=timeout)
            out = json.loads(p.stdout) if p.stdout.strip().startswith("{") else {"result": p.stdout[-500:] + p.stderr[-500:], "is_error": True}
        except subprocess.TimeoutExpired:
            out = {"result": "TIMEOUT", "is_error": True}
        out["wall_s"] = round(time.time() - start, 1)
        return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--layer", default="oriel", choices=list(LAYERS))
    ap.add_argument("--model", default="sonnet")
    ap.add_argument("--only")
    ap.add_argument("--trials", type=int, default=1)
    ap.add_argument("--timeout", type=int, default=420)
    ap.add_argument("--out", default="results.jsonl")
    args = ap.parse_args()
    if not HOST:
        sys.exit("set ORIEL_BENCH_SSH to the Windows PC's ssh name (and ORIEL_BENCH_DIR to its benchmark folder)")
    if " " in BENCH:
        sys.exit("ORIEL_BENCH_DIR can't contain spaces: the path goes through the PC's remote shell unquoted")
    layer = LAYERS[args.layer]
    ids = args.only.split(",") if args.only else list(TASKS)
    tunnel = None
    if layer.get("tunnel"):
        tunnel = subprocess.Popen(["ssh", "-N", "-o", "ExitOnForwardFailure=yes", *layer["tunnel"], HOST],
                                  stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        time.sleep(2)
    for trial in range(args.trials):
        for tid in ids:
            prompt_text, setup, check, teardown = TASKS[tid]
            setup()
            out = run_agent(PROMPT.format(hint=layer["hint"], task=prompt_text), layer, args.model, args.timeout)
            try:
                passed, detail = check(out.get("result", ""))
            except Exception as e:
                passed, detail = False, f"checker error: {e}"
            teardown()
            row = {"task": tid, "layer": args.layer, "model": args.model, "trial": trial, "passed": bool(passed), "detail": str(detail)[:200],
                   "turns": out.get("num_turns"), "wall_s": out.get("wall_s"), "api_s": round((out.get("duration_api_ms") or 0) / 1000, 1),
                   "cost_usd": out.get("total_cost_usd"), "error": out.get("is_error"), "result": (out.get("result") or "")[-400:]}
            print(f"{tid} {'PASS' if passed else 'FAIL'}  turns={row['turns']} wall={row['wall_s']}s api={row['api_s']}s  {row['detail'][:80]}", flush=True)
            with open(args.out, "a") as f:
                f.write(json.dumps(row) + "\n")
    if tunnel:
        tunnel.terminate()


if __name__ == "__main__":
    main()
