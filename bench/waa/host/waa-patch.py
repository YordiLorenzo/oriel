#!/usr/bin/env python3
"""Patch a WindowsAgentArena checkout so its VM setup completes in October 2026. Three things broke:

1. LibreOffice 24.8.2: every pinned mirror returns 404. The same version is on The Document Foundation's archive.
2. Caddy: setup downloads it from caddyserver.com's build API, which hung for over 30 minutes. Setup now copies the
   official GitHub release binary from the share (fetched here, checksum-verified) and skips the download.
3. pywin32 312 (what pip installs today) can't load win32ui without the MFC runtime, so the in-VM server dies at
   start. Setup now installs Microsoft's VC++ runtime before the server packages.

It also hides WAA's own server and proxy windows (on-logon.ps1): in a pilot run an agent typed into the server's
visible console and the server died, ending the run.

    python3 waa-patch.py /root/WindowsAgentArena

Idempotent. The golden image for the published run got the same fixes by hand, midway through setup.
"""
import hashlib
import io
import json
import pathlib
import sys
import urllib.request
import zipfile

CADDY = "https://github.com/caddyserver/caddy/releases/download/v2.11.7/caddy_2.11.7_windows_amd64.zip"
CADDY_SUMS = "https://github.com/caddyserver/caddy/releases/download/v2.11.7/caddy_2.11.7_checksums.txt"
LIBREOFFICE = "https://downloadarchive.documentfoundation.org/libreoffice/old/24.8.2.1/win/x86_64/LibreOffice_24.8.2.1_Win_x86-64.msi"
MARK = "# oriel-bench patch"

CADDY_BLOCK = f"""{MARK}: Caddy from its GitHub release on the share (caddyserver.com's build API hangs)
if (-not (Test-Path $caddyProxyExecutablePath)) {{
    Copy-Item "$scriptFolder\\caddy\\caddy.exe" $caddyProxyExecutablePath
    Add-Content -Path $PROFILE -Value "Set-Alias -Name $caddyProxyAlias -Value `"$caddyProxyExecutablePath`""
}}
"""

VCREDIST_BLOCK = f"""{MARK}: pywin32's win32ui needs the MFC runtime from Microsoft's VC++ redistributable
$vcRedist = "$env:TEMP\\vc_redist.x64.exe"
Invoke-WebRequest -UseBasicParsing https://aka.ms/vs/17/release/vc_redist.x64.exe -OutFile $vcRedist
Start-Process $vcRedist -ArgumentList '/install', '/quiet', '/norestart' -Wait

"""


LAUNCH_BLOCK = f"""        {MARK}: Chrome 136+ and Edge ignore --remote-debugging-port on their default profile, so every task's browser
        # launch uses its own profile folder (no spaces: some launches go through "start"), and the getters read it.
        if isinstance(command, list):
            def _profile(arg):
                for name, folder in (("chrome", r"C:\\Users\\Docker\\AppData\\Local\\Google\\Chrome\\UserDataWAA"),
                                     ("msedge", r"C:\\Users\\Docker\\AppData\\Local\\Microsoft\\Edge\\UserDataWAA")):
                    if name in arg:
                        return f"--user-data-dir={{folder}} --no-first-run --no-default-browser-check"
                return None
            for i, arg in enumerate(command):
                flags = _profile(arg) if isinstance(arg, str) else None
                if flags and "--user-data-dir" not in " ".join(command):
                    if " " in arg:  # cmd /c "start msedge": the browser's arguments go inside that string
                        command[i] = arg + " " + flags
                    else:
                        command.extend(flags.split())
                    break

"""


def patch_browser_profiles(desktop_env):
    """Point every task's Chrome and Edge at a profile folder of its own, and the getters at the same folder."""
    setup_py = desktop_env / "controllers/setup.py"
    s = setup_py.read_text()
    if MARK not in s:
        anchor = '        payload = json.dumps({"command": command, "shell": shell})\n        headers = {"Content-Type": "application/json"}\n\n        try:\n            response = requests.post(self.http_server + "/setup" + "/launch"'
        assert s.count(anchor) == 1, "launch anchor not found"
        setup_py.write_text(s.replace(anchor, LAUNCH_BLOCK + anchor))
    for name, old, new in (("chrome.py", "Chrome/User Data", "Chrome/UserDataWAA"), ("edge.py", "Edge/User Data", "Edge/UserDataWAA"),
                           ("msedge.py", "Edge/User Data", "Edge/UserDataWAA")):
        p = desktop_env / "evaluators/getters" / name
        g = p.read_text()
        g = g.replace(old, new)
        g = g.replace('"Google", "Chrome", "User Data"', '"Google", "Chrome", "UserDataWAA"')
        g = g.replace('"Microsoft", "Edge", "User Data"', '"Microsoft", "Edge", "UserDataWAA"')
        if name == "chrome.py":  # its fallback relaunch, when the agent closed the browser
            g = g.replace('"--remote-debugging-port=1337"\n                ]',
                          '"--remote-debugging-port=1337",\n                    r"--user-data-dir=C:\\Users\\Docker\\AppData\\Local\\Google\\Chrome\\UserDataWAA",\n                    "--no-first-run"\n                ]')
        p.write_text(g)


def main():
    root = pathlib.Path(sys.argv[1])
    setup_dir = root / "src/win-arena-container/vm/setup"

    cfg_path = setup_dir / "tools_config.json"
    cfg = json.loads(cfg_path.read_text())
    if cfg["LibreOffice"]["mirrors"][0] != LIBREOFFICE:
        cfg["LibreOffice"]["mirrors"].insert(0, LIBREOFFICE)
        cfg_path.write_text(json.dumps(cfg, indent=4))

    caddy_exe = setup_dir / "caddy/caddy.exe"
    if not caddy_exe.exists():
        blob = urllib.request.urlopen(CADDY).read()
        sums = urllib.request.urlopen(CADDY_SUMS).read().decode()
        want = next(line.split()[0] for line in sums.splitlines() if line.endswith("windows_amd64.zip"))
        got = hashlib.sha512(blob).hexdigest() if len(want) == 128 else hashlib.sha256(blob).hexdigest()
        if got != want:
            sys.exit(f"caddy checksum mismatch: {got} != {want}")
        caddy_exe.parent.mkdir(exist_ok=True)
        caddy_exe.write_bytes(zipfile.ZipFile(io.BytesIO(blob)).read("caddy.exe"))

    logon_path = setup_dir / "on-logon.ps1"
    logon = logon_path.read_text()
    if MARK not in logon:
        old_caddy = 'Start-Process -NoNewWindow -FilePath "powershell" -ArgumentList "-Command", "caddy reverse-proxy --from :9222 --to :1337"'
        old_server = "python $pythonScriptFile --port $pythonServerPort"
        assert logon.count(old_caddy) == 1 and logon.count(old_server) == 1, "on-logon anchors not found"
        logon = logon.replace(old_caddy, f"{MARK}: caddy and the server run hidden, so no agent can type into or close them\n"
                              'Start-Process -WindowStyle Hidden -FilePath "powershell" -ArgumentList "-Command", "caddy reverse-proxy --from :9222 --to :1337"')
        # (Get-Command python).Definition follows setup's profile alias to Python 3.10; Start-Process alone would search
        # PATH and pick GIMP's bundled Python 2.7.
        logon = logon.replace(old_server, 'Start-Process -WindowStyle Hidden -FilePath (Get-Command python).Definition -ArgumentList $pythonScriptFile, "--port", $pythonServerPort')
        logon_path.write_text(logon)

    patch_browser_profiles(root / "src/win-arena-container/client/desktop_env")

    setup_path = setup_dir / "setup.ps1"
    setup = setup_path.read_text()
    if MARK not in setup:
        anchor = "# Check if Caddy is already installed by checking the Caddy executable path"
        assert setup.count(anchor) == 1, "Caddy anchor not found"
        setup = setup.replace(anchor, CADDY_BLOCK + "\n" + anchor)
        anchor = "# - Windows Arena Server Setup"
        assert setup.count(anchor) == 1, "server anchor not found"
        setup = setup.replace(anchor, VCREDIST_BLOCK + anchor)
        setup_path.write_text(setup)
    print("patched")


if __name__ == "__main__":
    main()
