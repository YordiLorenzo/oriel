# /// script
# requires-python = ">=3.11"
# dependencies = ["mcp>=2.3", "httpx"]
# ///
"""Head-to-head: the same Windows UI tasks through four computer-use MCP servers on one Windows PC, timed from here.

    ORIEL_BENCH_SSH=<the PC's ssh name> uv run --script headtohead.py [--runs 5]
        [--tools oriel,cua,terminator,windows-mcp] [--json out.json]

Every tool runs in the PC's desktop session and every MCP client runs here, over the LAN: oriel through `ssh <PC>
oriel.exe mcp` (oriel.exe in $ORIEL_BENCH_DIR\\oriel, default C:\\oriel-bench), Cua Driver 0.38.0 through a loopback
relay to its stdio MCP (its daemon refuses the SSH session's pipe client), Terminator 0.24.28 and Windows-MCP 0.8.8
over Streamable HTTP through an SSH tunnel. The tunnels (local 23017 -> 3017 Terminator, 23018 -> 3018 Cua relay,
28765 -> 8765 Windows-MCP) must be up. A Windows-MCP server behind a bearer token gets it from $WINDOWS_MCP_TOKEN.

Tasks on Character Map (launched fresh per run, closed with taskkill outside the timing): launch it, read its tree,
fill "Characters to copy", press Select; then read the trees of an open Chrome and an open Edge window (read only).
Each tool gets its fastest honest settings: no screenshots, no highlighting, no post-action diffs (oriel's change
report is timed as an extra row). Outcomes are checked through a second oriel session, outside the timing.
Windows-MCP's click and type send real mouse/keyboard input; while the PC is locked that would type into the lock
screen, so those two are skipped unless the desktop is unlocked (--unlocked).
"""

import argparse
import asyncio
import json
import os
import re
import statistics
import subprocess
import sys
import time

import httpx
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client
from mcp.client.streamable_http import streamable_http_client

HOST = os.environ.get("ORIEL_BENCH_SSH", "")
ORIEL_EXE = os.environ.get("ORIEL_BENCH_DIR", r"C:\oriel-bench").rstrip("\\") + r"\oriel\oriel.exe"
ORIEL_MCP = StdioServerParameters(command="ssh", args=[HOST, ORIEL_EXE, "mcp"])
NOSHOT = {"include_window_screenshot": False, "include_monitor_screenshots": False}
TERM_ACTION = {"verify_element_exists": "", "verify_element_not_exists": "", "highlight_before_action": False,
               "ui_diff_before_after": False, **NOSHOT}


def text(res) -> str:
    return "\n".join(c.text for c in res.content if getattr(c, "type", "") == "text")


class Timer:
    def __init__(self):
        self.rows = []

    async def call(self, session, tool, op, name, args, ok=None):
        start = time.perf_counter()
        try:
            res = await session.call_tool(name, args)
            out, failed = text(res), res.is_error
        except Exception as e:  # a tool that crashes or times out still gets a row
            out, failed = f"{type(e).__name__}: {e}", True
        ms = (time.perf_counter() - start) * 1000
        good = not failed and (ok is None or ok(out))
        self.rows.append({"tool": tool, "op": op, "ms": round(ms, 1), "ok": good, "chars": len(out), "out": out[:300]})
        return out, good


def ssh(cmd: str) -> str:
    return subprocess.run(["ssh", HOST, cmd], capture_output=True, text=True, timeout=60).stdout


def kill_charmap():
    ssh("taskkill /im charmap.exe /f >NUL 2>&1")
    time.sleep(0.5)


def field_value(check: ClientSession):
    """Character Map's field through the referee's oriel session, outside the timing."""
    return check.call_tool("text", {"window": "Character Map", "name": "Characters to copy", "type": "Edit"})


async def verify(check: ClientSession, t: Timer, tool: str):
    """Untimed referee: after "fill abc" and "press Select" the field holds abc plus the selected character."""
    value = text(await field_value(check)).split("\n(")[0]
    t.rows.append({"tool": tool, "op": "outcome: field = abc + char", "ms": 0, "ok": "abc" in value and len(value.strip()) == 4,
                   "chars": len(value), "out": value})


async def wait_window(check: ClientSession, title: str, seconds: float = 10):
    """Untimed: wait until the launched window has its controls, so every tool starts the next task alike."""
    res = await check.call_tool("wait", {"window": title, "name": "Select", "timeout": seconds})
    return not res.is_error


async def run_oriel(s, t, check, runs):
    for _ in range(runs):
        kill_charmap()
        await t.call(s, "oriel", "launch", "launch", {"path": "charmap.exe", "wait_title": "Character Map", "tree": False},
                     lambda o: "Character Map" in o)
        await wait_window(check, "Character Map")
        out, _ = await t.call(s, "oriel", "tree charmap", "tree", {"window": "Character Map"}, lambda o: "Select" in o)
        # Like Cua's element tokens: act on the refs the tree just returned.
        edit = re.search(r'\[(\w+)\] Edit "Characters to copy', out)
        button = re.search(r'\[(\w+)\] Button "Select"', out)
        if edit and button:
            await t.call(s, "oriel", "fill field (ref)", "set", {"id": edit.group(1), "value": "abc", "changes": False})
            await t.call(s, "oriel", "press button (ref)", "invoke", {"id": button.group(1), "changes": False})
            await verify(check, t, "oriel")
        await t.call(s, "oriel", "fill field", "set", {"window": "Character Map", "name": "Characters to copy", "type": "Edit",
                     "value": "abc", "changes": False})
        await t.call(s, "oriel", "press button", "invoke", {"window": "Character Map", "name": "Select", "changes": False})
        await verify(check, t, "oriel")
        await t.call(s, "oriel", "press + change report", "invoke", {"window": "Character Map", "name": "Select"},
                     lambda o: "~" in o)
        await t.call(s, "oriel", "launch + tree (1 call)", "launch", {"path": "charmap.exe", "wait_title": "Character Map"},
                     lambda o: "Select" in o)
    for _ in range(runs):
        await t.call(s, "oriel", "tree chrome", "tree", {"window": "Google Chrome"}, lambda o: "Address" in o)
        await t.call(s, "oriel", "tree edge", "tree", {"window": "msedge"}, lambda o: "Address" in o)
        await t.call(s, "oriel", "list windows", "windows", {}, lambda o: "chrome" in o)


def cua_window(out: str, title: str):
    """The window whose title contains `title` (an app also has bubbles and popups, like Chrome's "Restore pages?")."""
    for m in re.finditer(r'pid (\d+)\) "([^"]*)" \[window_id: (\d+)\]', out):
        if title in m.group(2):
            return int(m.group(1)), int(m.group(3))
    return None, None


async def run_cua(s, t, check, runs):
    for _ in range(runs):
        kill_charmap()
        await t.call(s, "cua", "launch", "launch_app", {"path": r"C:\Windows\System32\charmap.exe"})
        await wait_window(check, "Character Map")
        out, _ = await t.call(s, "cua", "list windows (charmap)", "list_windows", {"app": "charmap"}, lambda o: "window_id" in o)
        pid, wid = cua_window(out, "Character Map")
        out, _ = await t.call(s, "cua", "tree charmap", "get_window_state", {"pid": pid, "window_id": wid, "include_screenshot": False},
                              lambda o: "Select" in o)
        snap = re.search(r"snapshot_id=(\S+)", out)
        edit = re.search(r"\[(\d+)\] Edit \"Characters to copy", out)
        button = re.search(r"\[(\d+)\] Button \"Select\"", out)
        if snap and edit and button:
            await t.call(s, "cua", "fill field", "set_value", {"element_token": f"{snap.group(1)}:{edit.group(1)}", "value": "abc"})
            await t.call(s, "cua", "press button", "click", {"element_token": f"{snap.group(1)}:{button.group(1)}"})
            await verify(check, t, "cua")
    for _ in range(runs):
        for op, app, title in (("tree chrome", "chrome", "Google Chrome"), ("tree edge", "msedge", "Microsoft")):
            out, _ = await t.call(s, "cua", f"list windows ({app})", "list_windows", {"app": app}, lambda o: "window_id" in o)
            pid, wid = cua_window(out, title)
            await t.call(s, "cua", op, "get_window_state", {"pid": pid, "window_id": wid, "include_screenshot": False},
                         lambda o: "Address" in o)
        await t.call(s, "cua", "list windows", "list_windows", {}, lambda o: "chrome" in o)


async def run_terminator(s, t, check, runs):
    for _ in range(runs):
        kill_charmap()
        await t.call(s, "terminator", "launch", "open_application", {"app_name": "charmap", "verify_element_exists": "",
                     "verify_element_not_exists": "", "include_tree_after_action": False, **NOSHOT})
        await wait_window(check, "Character Map")
        await t.call(s, "terminator", "tree charmap", "get_window_tree", {"process": "charmap", "include_tree_after_action": True,
                     "include_window_screenshot": False}, lambda o: "Select" in o)
        await t.call(s, "terminator", "fill field", "set_value", {"process": "charmap", "selector": "role:Edit && name:Characters to copy",
                     "value": "abc", **TERM_ACTION})
        await t.call(s, "terminator", "press button", "invoke_element", {"process": "charmap", "selector": "role:Button && name:Select",
                     **TERM_ACTION})
        await verify(check, t, "terminator")
    for _ in range(runs):
        await t.call(s, "terminator", "tree chrome", "get_window_tree", {"process": "chrome", "include_tree_after_action": True,
                     "include_window_screenshot": False}, lambda o: "Address" in o)
        await t.call(s, "terminator", "tree edge", "get_window_tree", {"process": "msedge", "include_tree_after_action": True,
                     "include_window_screenshot": False}, lambda o: "Address" in o)
        await t.call(s, "terminator", "list windows", "get_applications_and_windows_list", {}, lambda o: "chrome" in o.lower())


async def run_windows_mcp(s, t, check, runs, unlocked):
    snapshot = {"use_vision": False, "use_dom": False, "use_annotation": False, "use_ui_tree": True}
    for _ in range(runs):
        kill_charmap()
        await t.call(s, "windows-mcp", "launch", "App", {"mode": "launch", "name": "charmap"})
        await wait_window(check, "Character Map")
        await t.call(s, "windows-mcp", "tree charmap (desktop snapshot)", "Snapshot", snapshot, lambda o: "Character Map" in o)
        if unlocked:  # real mouse and keyboard input: only on an unlocked desktop
            await t.call(s, "windows-mcp", "fill field", "Type", {"loc": [241, 418], "text": "abc", "clear": True})
            await t.call(s, "windows-mcp", "press button", "Click", {"loc": [367, 417]})
            await verify(check, t, "windows-mcp")


async def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--runs", type=int, default=5)
    ap.add_argument("--tools", default="oriel,cua,terminator,windows-mcp")
    ap.add_argument("--unlocked", action="store_true", help="the desktop is unlocked: also run Windows-MCP's input tools")
    ap.add_argument("--json")
    args = ap.parse_args()
    if not HOST:
        sys.exit("set ORIEL_BENCH_SSH to the Windows PC's ssh name (and ORIEL_BENCH_DIR to its benchmark folder)")
    if " " in ORIEL_EXE:
        sys.exit("ORIEL_BENCH_DIR can't contain spaces: the path goes through the PC's remote shell unquoted")
    t = Timer()
    async with stdio_client(ORIEL_MCP) as (cr, cw):
        async with ClientSession(cr, cw) as check:  # the referee: waits for windows and reads outcomes
            await check.initialize()
            for tool in args.tools.split(","):
                print(f"== {tool}", file=sys.stderr)
                try:
                    if tool == "oriel":
                        async with stdio_client(ORIEL_MCP) as (r, w):
                            async with ClientSession(r, w) as s:
                                await s.initialize()
                                await run_oriel(s, t, check, args.runs)
                    elif tool == "cua":
                        async with stdio_client(StdioServerParameters(command="nc", args=["127.0.0.1", "23018"])) as (r, w):
                            async with ClientSession(r, w) as s:
                                await s.initialize()
                                await run_cua(s, t, check, args.runs)
                    else:
                        url = {"terminator": "http://127.0.0.1:23017/mcp", "windows-mcp": "http://127.0.0.1:28765/mcp"}[tool]
                        token = os.environ.get("WINDOWS_MCP_TOKEN") if tool == "windows-mcp" else None
                        headers = {"Authorization": "Bearer " + token} if token else {}
                        async with httpx.AsyncClient(headers=headers, timeout=180) as client:
                            async with streamable_http_client(url, http_client=client) as (r, w, *_):
                                async with ClientSession(r, w) as s:
                                    await s.initialize()
                                    if tool == "terminator":
                                        await run_terminator(s, t, check, args.runs)
                                    else:
                                        await run_windows_mcp(s, t, check, min(args.runs, 3), args.unlocked)
                except Exception as e:
                    print(f"   {tool} aborted: {type(e).__name__}: {e}", file=sys.stderr)
            kill_charmap()

    print(f"{'tool':<12} {'operation':<32} {'n':>3} {'ok':>3} {'p50 ms':>8} {'p90 ms':>8} {'chars':>7}")
    seen = []
    for r in t.rows:
        if (r["tool"], r["op"]) not in seen:
            seen.append((r["tool"], r["op"]))
    for tool, op in seen:
        rows = [r for r in t.rows if (r["tool"], r["op"]) == (tool, op)]
        ms = sorted(r["ms"] for r in rows)
        p90 = ms[min(len(ms) - 1, round(0.9 * (len(ms) - 1)))]
        print(f"{tool:<12} {op:<32} {len(rows):>3} {sum(r['ok'] for r in rows):>3} {statistics.median(ms):>8.0f} {p90:>8.0f}"
              f" {statistics.median(r['chars'] for r in rows):>7.0f}")
    if args.json:
        with open(args.json, "w") as f:
            json.dump(t.rows, f, indent=1)


if __name__ == "__main__":
    asyncio.run(main())
