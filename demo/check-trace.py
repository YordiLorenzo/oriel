#!/usr/bin/env python3
"""check-trace.py: check that a Claude Code session drove Windows with oriel's element actions only.

    python check-trace.py [session.jsonl | folder]

With no argument it reads the newest session under ~/.claude/projects ($CLAUDE_CONFIG_DIR/projects when that is set);
with a folder, the newest session in it. It also reads the session's subagents (<session>/subagents/**/*.jsonl).

It fails (exit 1) when the session made any of these calls:
  - oriel's keys, click or drag: they send real keyboard or mouse input;
  - any other oriel tool that is not a read or an element action (focus, window, launch, close), including inside a batch;
  - any tool of another MCP server: the demo session has only oriel, and other servers type and click under other
    names (Type, Shortcut, Move, vnc_key, browser_press_key);
  - a shell (Bash, PowerShell, Monitor): a command can send keys or clicks too, for example `oriel call keys`;
and when it finds no element action at all (the wrong session, or a check that compared nothing).
Reads and element actions pass: status, windows, tree, find, text, wait, screenshot; invoke, set, select, scroll.
"""
import json
import os
import sys
from pathlib import Path

READS = {"status", "windows", "tree", "find", "text", "wait", "screenshot"}
ELEMENT_ACTIONS = {"invoke", "set", "select", "scroll"}
INPUT = {"keys", "click", "drag"}
SHELLS = {"Bash", "PowerShell", "Monitor"}


def newest_session(folder):
    sessions = [p for p in folder.rglob("*.jsonl") if "subagents" not in p.parts]
    if not sessions:
        sys.exit("no session (.jsonl) under " + str(folder))
    return max(sessions, key=lambda p: p.stat().st_mtime)


def tool_uses(value):
    """Every tool_use block anywhere in one parsed line (assistant messages, and request logs that repeat them)."""
    if isinstance(value, dict):
        if value.get("type") == "tool_use" and isinstance(value.get("name"), str):
            yield value
        for v in value.values():
            yield from tool_uses(v)
    elif isinstance(value, list):
        for v in value:
            yield from tool_uses(v)


def problem(name, args):
    """Why this call breaks "element actions only", or None when it is fine."""
    if name in SHELLS:
        return "a shell command, which can send keys or clicks"
    if not name.startswith("mcp__"):
        return None
    server, _, tool = name[len("mcp__"):].rpartition("__")
    if "oriel" not in server:
        return "a tool of another MCP server, not oriel"
    if tool in INPUT:
        return "sends real keyboard or mouse input"
    if tool == "batch":
        steps = args.get("steps") if isinstance(args, dict) else None
        if not isinstance(steps, list):
            return "a batch without a readable list of steps"
        bad = [str(s.get("action") if isinstance(s, dict) else s) for s in steps
               if not isinstance(s, dict) or s.get("action") not in READS | ELEMENT_ACTIONS]
        return "a batch with " + ", ".join(bad) if bad else None
    if tool not in READS | ELEMENT_ACTIONS:
        return "not a read or an element action"
    return None


def element_actions(name, args):
    if not name.startswith("mcp__") or "oriel" not in name:
        return 0
    tool = name.rpartition("__")[2]
    if tool == "batch" and isinstance(args, dict) and isinstance(args.get("steps"), list):
        return sum(1 for s in args["steps"] if isinstance(s, dict) and s.get("action") in ELEMENT_ACTIONS)
    return 1 if tool in ELEMENT_ACTIONS else 0


def main():
    if len(sys.argv) > 2:
        sys.exit(__doc__)
    home = Path(os.environ.get("CLAUDE_CONFIG_DIR") or Path.home() / ".claude")
    target = Path(sys.argv[1]) if len(sys.argv) == 2 else home / "projects"
    session = newest_session(target) if target.is_dir() else target
    files = [session] + sorted((session.parent / session.stem / "subagents").rglob("*.jsonl"))

    seen, counts, failures, lines, actions = set(), {}, [], 0, 0
    for path in files:
        with open(path, encoding="utf-8", errors="replace") as f:
            for number, line in enumerate(f, 1):
                lines += 1
                try:
                    entry = json.loads(line)
                except ValueError:
                    continue
                for use in tool_uses(entry):
                    key = use.get("id") or (str(path), number, use["name"])
                    if key in seen:
                        continue
                    seen.add(key)
                    name, args = use["name"], use.get("input") or {}
                    counts[name] = counts.get(name, 0) + 1
                    actions += element_actions(name, args)
                    why = problem(name, args)
                    if why:
                        failures.append("  %s:%d %s (%s) %s" % (path.name, number, name, why, json.dumps(args)[:160]))

    print("read %s: %d file(s), %d lines, %d tool calls, %d element actions" % (session, len(files), lines, len(seen), actions))
    for name in sorted(counts):
        print("  %4d  %s" % (counts[name], name))
    if failures:
        print("FAIL: %d call(s) are not element actions or reads; record again:" % len(failures))
        print("\n".join(failures))
        return 1
    if actions == 0:
        print("FAIL: no oriel element action in this session; is it the right file?")
        return 1
    print("PASS: element actions only (no keys, click, drag, window, shell or other MCP server calls)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
