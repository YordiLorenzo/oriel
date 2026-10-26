#!/usr/bin/env python3
"""latency.py: time oriel's daemon action by action, over SSH, the way `ssh pc oriel mcp` reaches it.

    python3 latency.py --host my-windows-pc [--exe C:\\path\\oriel.exe] [--runs 5] [--only charmap,read] [--json out.json]

Opens an SSH tunnel to the daemon (127.0.0.1:47665 on the PC; start it once with `oriel install`, or any `oriel mcp`)
and sends every request over one connection. For every step it records the wall time here and the time the daemon
reports, so the difference is tunnel plus network, and the reply size (about 4 bytes per model token). Scenarios only
change windows they opened: a launch binds later steps to the new window's handle, and a run is skipped when a window
with that title is already open. They never type into a window that holds your text. Run it when nobody uses the PC.
While the PC is locked, Store apps are suspended, so those scenarios are skipped.
"""

import argparse
import base64
import hashlib
import hmac
import json
import os
import re
import socket
import statistics
import subprocess
import sys
import time

EOT = b"<EOT>\n"
TOOK = re.compile(r"\n?\((\S+) took (\d+) ms\)$")


HOST = "localhost"
EXE = r"%LOCALAPPDATA%\Programs\oriel\oriel.exe"


def tunnel(remote: int) -> tuple[subprocess.Popen, int]:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        port = s.getsockname()[1]
    proc = subprocess.Popen(["ssh", "-N", "-o", "ExitOnForwardFailure=yes", "-L", f"127.0.0.1:{port}:127.0.0.1:{remote}",
                             HOST], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        try:
            socket.create_connection(("127.0.0.1", port), timeout=0.2).close()
            return proc, port
        except OSError:
            time.sleep(0.05)
    proc.terminate()
    sys.exit(f"tunnel to {HOST} did not come up")


class Daemon:
    def __init__(self, port: int, fresh: bool):
        self.port, self.fresh, self.sock = port, fresh, None
        # The daemon's per-user key, read once over SSH, for the handshake (it is never sent to the daemon).
        self.key = subprocess.run(["ssh", HOST, r"type %LOCALAPPDATA%\oriel\token"], capture_output=True, text=True, timeout=30).stdout.strip()
        self.auth = None

    def mac(self, message: str) -> str:
        return base64.b64encode(hmac.new(self.key.encode(), message.encode(), hashlib.sha256).digest()).decode()

    def handshake(self):
        nonce = base64.b64encode(os.urandom(16)).decode()
        self.sock.sendall((json.dumps({"hello": nonce}) + "\n").encode())
        line = b""
        while not line.endswith(b"\n"):
            chunk = self.sock.recv(1)
            if not chunk:
                raise ConnectionError("the daemon closed the connection during the handshake")
            line += chunk
        answer = json.loads(line)
        if answer.get("proof") != self.mac("oriel-daemon\n" + nonce):
            sys.exit("the process on that port is not this user's oriel daemon")
        self.auth = self.mac("oriel-client\n" + answer["nonce"])

    def call(self, payload: dict) -> tuple[float, int | None, str]:
        start = time.perf_counter()
        if self.sock is None:
            self.sock = socket.create_connection(("127.0.0.1", self.port), timeout=120)
            self.sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            self.handshake()
        self.sock.sendall((json.dumps({**payload, "Auth": self.auth}) + "\n").encode())
        buf = b""
        while not buf.endswith(EOT):
            chunk = self.sock.recv(65536)
            if not chunk:
                raise ConnectionError("daemon closed the connection before <EOT>")
            buf += chunk
        if self.fresh:
            self.sock.close()
            self.sock = None
        wall = (time.perf_counter() - start) * 1000
        text = buf[: -len(EOT)].decode().rstrip()
        m = TOOK.search(text)
        return wall, (int(m.group(2)) if m else None), (text[: m.start()] if m else text)


def step(label, action, check=None, pause=0.0, **opts):
    """pause: seconds to wait first (not timed), e.g. the model's turn before a call. Id="$ref" takes the first
    ref of the previous reply."""
    return {"label": label, "payload": {"action": action, **opts}, "check": check, "pause": pause}


def launched_title(steps: list[dict]) -> str | None:
    return next((st["payload"]["WaitTitle"] for st in steps if st["payload"].get("WaitTitle")), None)


# Steps inherit the batch's Window, which the run binds to the handle its own launch returned.
CHARMAP_BATCH = [
    {"action": "set", "Name": "Characters to copy", "Type": "Edit", "Value": "abc"},
    {"action": "invoke", "Name": "Select"},
    {"action": "text", "Name": "Characters to copy", "Type": "Edit"},
    {"action": "close"},
]

# Each scenario: steps run in order, once per run. `check` is a substring the reply must contain.
NEEDS_UNLOCK = {"calc", "settings"}
SCENARIOS = {
    "floor": [
        step("ping", "ping", "pong"),
        step("windows", "windows", "["),
    ],
    "charmap": [
        step("launch charmap", "launch", "Character Map", Path="charmap.exe", WaitTitle="Character Map"),
        step("tree charmap", "tree", "Select", Window="Character Map"),
        step("set chars (no report)", "set", "set", Window="Character Map", Name="Characters to copy", Type="Edit", Value="abc", Changes=False),
        step("invoke Select (no report)", "invoke", "Select", Window="Character Map", Name="Select", Changes=False),
        step("set chars", "set", "set", Window="Character Map", Name="Characters to copy", Type="Edit", Value="abc"),
        step("invoke Select", "invoke", "Select", Window="Character Map", Name="Select"),
        step("find Select", "find", "Select", Window="Character Map", Name="Select", Type="Button"),
        step("invoke ref (no report)", "invoke", "Select", Id="$ref", Changes=False),
        step("text chars", "text", "abc", Window="Character Map", Name="Characters to copy", Type="Edit"),
        step("miss (typo)", "invoke", "Select", pause=2.5, Window="Character Map", Name="Selcet"),
        step("close charmap", "close", "closed", Window="Character Map"),
    ],
    "batch": [
        step("launch charmap + tree", "launch", "Select", Path="charmap.exe", WaitTitle="Character Map", Tree=True),
        step("charmap batch", "batch", "abc", Window="Character Map", Steps=CHARMAP_BATCH),
    ],
    "calc": [
        step("launch calc", "launch", "Calculator", Path="calc.exe", WaitTitle="Calculator"),
        step("tree calc", "tree", "Seven", Window="Calculator"),
        step("invoke Clear", "invoke", "invoked", Window="Calculator", Name="Clear"),
        step("invoke Seven", "invoke", "invoked", Window="Calculator", Name="Seven"),
        step("invoke Plus", "invoke", "invoked", Window="Calculator", Name="Plus"),
        step("invoke Eight", "invoke", "invoked", Window="Calculator", Name="Eight"),
        step("invoke Equals", "invoke", "invoked", Window="Calculator", Name="Equals"),
        step("find result", "find", "Display is 15", Window="Calculator", Name="Display is"),
        step("close calc", "close", "closed", Window="Calculator"),
    ],
    "settings": [
        step("launch settings", "launch", "Settings", Path="ms-settings:about", WaitTitle="Settings"),
        step("wait specs", "wait", "Device specifications", Window="Settings", Name="Device specifications"),
        step("tree settings", "tree", "System", Window="Settings"),
        step("find search box", "find", "Edit", Window="Settings", Name="Find a setting", Type="Edit"),
        step("set search box", "set", "set", Window="Settings", Name="Find a setting", Type="Edit", Value="display"),
        step("close settings", "close", "closed", Window="Settings"),
    ],
    "explorer": [
        step("launch explorer", "launch", "Web", Path="explorer.exe", Args=r"C:\Windows\Web", WaitTitle="Web"),
        step("tree explorer", "tree", "Wallpaper", Window="Web"),
        step("find a folder", "find", "Wallpaper", Window="Web", Name="Wallpaper", Type="ListItem"),
        step("tree explorer again", "tree", "Wallpaper", Window="Web"),
        step("close explorer", "close", "closed", Window="Web"),
    ],
    "read": [
        step("tree chrome", "tree", "Chrome", Window="chrome"),
        step("tree edge", "tree", "Edge", Window="msedge"),
        step("find edge tabs", "find", "TabItem", Window="msedge", Name="", Type="TabItem"),
    ],
}


def main() -> None:
    global HOST, EXE
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--runs", type=int, default=5)
    ap.add_argument("--only", help="comma-separated scenarios: " + ",".join(SCENARIOS))
    ap.add_argument("--port", type=int, help="use an existing tunnel to the daemon instead of opening one")
    ap.add_argument("--host", required=True, help="the Windows PC's ssh name")
    ap.add_argument("--exe", default=EXE, help="oriel.exe on the PC (for its token)")
    ap.add_argument("--remote-port", type=int, help="the daemon's port on the PC (default: what `oriel port` says)")
    ap.add_argument("--fresh", action="store_true", help="open a new connection per request (the old protocol)")
    ap.add_argument("--json", help="write every sample to this file")
    args = ap.parse_args()
    names = args.only.split(",") if args.only else list(SCENARIOS)
    HOST, EXE = args.host, args.exe

    # Any oriel call over SSH starts the daemon in the desktop session if it isn't running.
    subprocess.run(["ssh", HOST, f'"{EXE}" call ping'], capture_output=True, timeout=60)
    if args.remote_port is None:
        args.remote_port = int(subprocess.run(["ssh", HOST, f'"{EXE}" port'], capture_output=True, text=True, timeout=30).stdout.strip())
    proc, port = (None, args.port) if args.port else tunnel(args.remote_port)
    daemon = Daemon(port, args.fresh)
    samples = []
    try:
        status = daemon.call({"action": "status"})[2]
        print(f"{HOST}: {status}", file=sys.stderr)
        if status.startswith("locked"):
            skipped = [n for n in names if n in NEEDS_UNLOCK]
            names = [n for n in names if n not in NEEDS_UNLOCK]
            if skipped:
                print(f"skipping {', '.join(skipped)}: Store apps are suspended while the PC is locked", file=sys.stderr)
        for name in names:
            title = launched_title(SCENARIOS[name])
            for run in range(args.runs):
                if title and any(title.lower() in line.split('"', 1)[-1].lower()
                                 for line in daemon.call({"action": "windows"})[2].splitlines()):
                    print(f"  {name} run {run}: a window titled like '{title}' is already open; skipped", file=sys.stderr)
                    continue
                bound, last = None, ""  # the handle of the window this run launched; the previous reply
                for st in SCENARIOS[name]:
                    payload = dict(st["payload"])
                    if bound and payload.get("Window") == title:
                        payload["Window"] = bound
                    if payload.get("Id") == "$ref":
                        m = re.search(r"\[([a-z]{2,3}\d+)\]", last)
                        payload["Id"] = m.group(1) if m else "e0"
                    time.sleep(st["pause"])
                    wall, server, text = daemon.call(payload)
                    last = text
                    if payload["action"] == "launch" and payload.get("WaitTitle"):
                        m = re.search(r"window \[(\d+)\]", text)
                        if "reused window" in text or not m:
                            print(f"  {name} run {run}: launch gave no new window ({text[:120]!r}); rest skipped", file=sys.stderr)
                            break
                        bound = m.group(1)
                    ok = (st["check"] is None or st["check"] in text) and (
                        not text.startswith("ERROR") or st["label"].startswith("miss"))
                    samples.append({"scenario": name, "label": st["label"], "run": run, "wall_ms": round(wall, 1),
                                    "server_ms": server, "ok": ok, "lines": text.count("\n") + 1, "bytes": len(text)})
                    if not ok:
                        print(f"  {name}/{st['label']} run {run}: {text[:200]!r}", file=sys.stderr)
                time.sleep(0.3)  # let closed windows go away before the next launch
    finally:
        if proc:
            proc.terminate()

    print(f"{'step':<26} {'n':>3} {'ok':>3} {'wall p50':>9} {'wall p90':>9} {'daemon p50':>11} {'net p50':>8} {'bytes':>6}")
    seen = []
    for s in samples:
        key = (s["scenario"], s["label"])
        if key not in seen:
            seen.append(key)
    for key in seen:
        rows = [s for s in samples if (s["scenario"], s["label"]) == key]
        walls = sorted(r["wall_ms"] for r in rows)
        servers = [r["server_ms"] for r in rows if r["server_ms"] is not None]
        p90 = walls[min(len(walls) - 1, round(0.9 * (len(walls) - 1)))]
        srv = statistics.median(servers) if servers else float("nan")
        print(f"{key[1]:<26} {len(rows):>3} {sum(r['ok'] for r in rows):>3} {statistics.median(walls):>9.0f} {p90:>9.0f}"
              f" {srv:>11.0f} {statistics.median(walls) - srv:>8.0f} {statistics.median(r['bytes'] for r in rows):>6.0f}")
    if args.json:
        with open(args.json, "w") as f:
            json.dump(samples, f, indent=1)


if __name__ == "__main__":
    main()
