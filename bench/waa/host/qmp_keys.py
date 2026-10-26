#!/usr/bin/env python3
"""Type into the WAA VM through QEMU's monitor (QMP on 127.0.0.1:7200 in the container), for setup steps before any
in-VM channel exists. Args: '@combo' sends a chord (e.g. @meta_l-r, @ret), anything else is typed as US-layout text."""
import json, socket, sys, time

SHIFTED = {'|': 'backslash', '"': 'apostrophe', ':': 'semicolon', '>': 'dot', '<': 'comma', '_': 'minus', '{': 'bracket_left',
           '}': 'bracket_right', '$': '4', '(': '9', ')': '0', '?': 'slash', '+': 'equal', '*': '8', '&': '7', '%': '5',
           '@': '2', '!': '1', '#': '3', '^': '6', '~': 'grave_accent'}
PLAIN = {' ': 'spc', '-': 'minus', '.': 'dot', '\\': 'backslash', '=': 'equal', '/': 'slash', ',': 'comma', "'": 'apostrophe',
         ';': 'semicolon', '[': 'bracket_left', ']': 'bracket_right', '`': 'grave_accent', '\n': 'ret'}

s = socket.create_connection(("127.0.0.1", 7200))
f = s.makefile("rw")
f.readline()
def cmd(c):
    f.write(json.dumps(c) + "\n"); f.flush()
    while True:
        r = json.loads(f.readline())
        if "return" in r or "error" in r:
            return r
cmd({"execute": "qmp_capabilities"})
def send(keys):
    cmd({"execute": "send-key", "arguments": {"keys": [{"type": "qcode", "data": k} for k in keys]}})
    time.sleep(0.03)
for arg in sys.argv[1:]:
    if arg.startswith("@"):
        send(arg[1:].split("-")); time.sleep(0.8)
        continue
    for ch in arg:
        if ch.isalpha():
            send(["shift", ch.lower()] if ch.isupper() else [ch])
        elif ch.isdigit():
            send([ch])
        elif ch in SHIFTED:
            send(["shift", SHIFTED[ch]])
        else:
            send([PLAIN[ch]])
