#!/usr/bin/env python3
"""Round 10's start check on firecracker: waa-round10.timer starts it (as waa-round10.service) at 00:00 on Fri 13 Nov
2026. Once a minute it checks the three conditions below and logs one line; when all three hold at one check it
starts round10.sh in its place. At DEADLINE it gives up, logs why and exits 1.

1. Power: every reading of Home Assistant's sensor.firecracker_power stayed under LIMIT_W for CLEAR_MINUTES minutes
   in a row. The readings come from the MQTT topic that sensor reads (this machine's power-publisher sends one every
   30 s), with the publisher's own login: Home Assistant's API token is not on this machine. A minute without a
   reading starts the count again.
2. Memory: MemAvailable is at least MEM_GIB.
3. Nothing in the way: no WAA container (waa<i>, winarena), no other round 10 run, and DISK_GB free.

Usage: round10-gate.py [--dry-run]   (--dry-run: check and log, but never start round10.sh)
"""
import datetime
import json
import os
import re
import select
import shutil
import socket
import struct
import subprocess
import sys
import time

LIMIT_W = 700
CLEAR_MINUTES = 15
DEADLINE = datetime.datetime(2026, 11, 13, 3, 0)  # local time
MEM_GIB = 90   # 10 VMs of 8 GiB, plus QEMU's own memory per VM and 10 agents
DISK_GB = 200  # 10 VM folders of about 18 GB each
TOPIC = "homelab/power/firecracker/state"
H = os.environ.get("WAA_HOME", os.path.expanduser("~/waa"))


def log(message):
    print(f"{datetime.datetime.now():%Y-%m-%d %H:%M:%S} gate: {message}", flush=True)


def mqtt_str(s):
    b = s.encode()
    return struct.pack("!H", len(b)) + b


def mqtt_packet(kind, body):  # the fixed header and remaining length of MQTT 3.1.1, as in power-publisher.py
    n, length = len(body), b""
    while True:
        byte, n = n % 128, n // 128
        length += bytes([byte | (0x80 if n else 0)])
        if not n:
            return bytes([kind]) + length + body


def recv_exact(sock, n):
    data = b""
    while len(data) < n:
        chunk = sock.recv(n - len(data))
        if not chunk:
            raise ConnectionError("the broker closed the connection")
        data += chunk
    return data


def read_packet(sock):
    kind, n, shift = recv_exact(sock, 1)[0], 0, 0
    while True:
        byte = recv_exact(sock, 1)[0]
        n |= (byte & 0x7F) << shift
        shift += 7
        if not byte & 0x80:
            return kind, recv_exact(sock, n)


def subscribe():
    with open(os.path.expanduser("~/.config/power-publisher/mqtt")) as f:
        host, password = f.read().split()[:2]
    sock = socket.create_connection((host, 1883), timeout=15)
    flags = 0x80 | 0x40 | 0x02  # user name, password, clean session
    sock.sendall(mqtt_packet(0x10, mqtt_str("MQTT") + bytes([4, flags]) + struct.pack("!H", 90)
                             + mqtt_str("waa-round10-gate") + mqtt_str("power") + mqtt_str(password)))
    kind, body = read_packet(sock)
    if kind != 0x20 or body[1] != 0:
        raise ConnectionError(f"the broker refused the login: {kind:#x} {body!r}")
    sock.sendall(mqtt_packet(0x82, struct.pack("!H", 1) + mqtt_str(TOPIC) + b"\x00"))  # SUBSCRIBE, QoS 0
    return sock


def readings(sock, seconds):
    """The watts of every state message in the next `seconds`."""
    end, watts = time.monotonic() + seconds, []
    while (left := end - time.monotonic()) > 0:
        if select.select([sock], [], [], left)[0]:
            kind, body = read_packet(sock)
            if kind >> 4 == 3:  # PUBLISH at QoS 0: topic, then the payload
                watts.append(json.loads(body[2 + struct.unpack("!H", body[:2])[0]:])["w"])
    sock.sendall(b"\xc0\x00")  # PINGREQ: the login's keepalive is 90 s
    return watts


def mem_available_gib():
    with open("/proc/meminfo") as f:
        kib = next(int(line.split()[1]) for line in f if line.startswith("MemAvailable:"))
    return kib / 2**20


def other_runs():
    """Other copies of the run: round10.sh, or run.py of bench/waa."""
    found = []
    for pid in filter(str.isdigit, os.listdir("/proc")):
        try:
            with open(f"/proc/{pid}/cmdline") as f:
                argv = f.read().split("\0")
        except OSError:  # the process ended meanwhile
            continue
        if any(a.endswith("round10.sh") for a in argv) or ("run.py" in argv and "--layer" in argv):
            found.append(pid)
    return found


def in_the_way():
    found = []
    ps = subprocess.run(["docker", "ps", "--format", "{{.Names}}"], capture_output=True, text=True)
    if ps.returncode:
        found.append("docker doesn't answer: " + ps.stderr.strip())
    waa = [n for n in ps.stdout.split() if re.fullmatch(r"waa\d+|winarena", n)]
    if waa:
        found.append("WAA containers running: " + " ".join(waa))
    if runs := other_runs():
        found.append("another run is running: PIDs " + " ".join(runs))
    free = shutil.disk_usage(H).free / 1e9
    if free < DISK_GB:
        found.append(f"only {free:.0f} GB free disk (need {DISK_GB})")
    return found


def snapshot():
    """What else runs on the machine: load, memory, the busiest processes and the containers."""
    with open("/proc/loadavg") as f:
        load = " ".join(f.read().split()[:3])
    top = subprocess.run(["ps", "-eo", "pcpu,rss,user:12,comm", "--sort=-pcpu"], capture_output=True, text=True).stdout
    names = subprocess.run(["docker", "ps", "--format", "{{.Names}}"], capture_output=True, text=True).stdout.split()
    lines = [f"load {load}; memory {mem_available_gib():.0f} GiB free; disk {shutil.disk_usage(H).free / 1e9:.0f} GB "
             f"free; containers: {' '.join(names) or 'none'}; busiest processes (CPU %, RSS KiB):"]
    return "\n".join(lines + ["    " + line for line in top.splitlines()[1:7]])


def main():
    dry_run = "--dry-run" in sys.argv
    log(f"starts round10.sh once sensor.firecracker_power stays under {LIMIT_W} W for {CLEAR_MINUTES} minutes in a "
        f"row, {MEM_GIB} GiB of memory is free and nothing is in the way; checks each minute until "
        f"{DEADLINE:%a %d %b %H:%M}" + (" (dry run: never starts it)" if dry_run else ""))
    log(snapshot())
    sock, streak = None, 0
    while True:
        error = ""
        try:
            sock = sock or subscribe()
            watts = readings(sock, 60)
        except (OSError, ValueError, KeyError) as e:  # ConnectionError and socket timeouts are OSErrors
            error, watts = f" ({type(e).__name__}: {e})", []
            if sock:
                sock.close()
            sock = None
            time.sleep(30)
        streak = streak + 1 if watts and max(watts) < LIMIT_W else 0
        mem, blockers = mem_available_gib(), in_the_way()
        power = f"max {max(watts):.0f} W over {len(watts)} readings" if watts else "no reading" + error
        log(f"power {power}, under {LIMIT_W} W for {streak} min in a row; memory {mem:.0f} GiB free (need "
            f"{MEM_GIB}); " + ("; ".join(blockers) or "nothing in the way"))
        if streak >= CLEAR_MINUTES and mem >= MEM_GIB and not blockers:
            log("all clear: " + ("dry run, not starting round10.sh" if dry_run else "starting round10.sh"))
            log(snapshot())
            if dry_run:
                return
            sock.close()
            os.execv("/bin/bash", ["bash", f"{H}/round10.sh"])
        if datetime.datetime.now() >= DEADLINE:
            why = [f"power under {LIMIT_W} W for only {streak} of {CLEAR_MINUTES} minutes"] if streak < CLEAR_MINUTES else []
            why += [f"only {mem:.0f} GiB memory free"] if mem < MEM_GIB else []
            log(f"not started: it is past {DEADLINE:%H:%M} and at the last check " + "; ".join(why + blockers))
            sys.exit(1)


if __name__ == "__main__":
    main()
