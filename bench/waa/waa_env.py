#!/usr/bin/env python3
"""WindowsAgentArena's own task setup and scoring, without its agent loop. Runs inside the WAA container, from /client:

    python waa_env.py reset <domain> <task_id>          close apps, run the task's setup in the VM, print the instruction
    python waa_env.py eval <domain> <task_id> [--fail]  run the task's evaluator unchanged, print the score

--fail records that the agent declared the task infeasible (WAA's "FAIL" action), which is how WAA scores infeasible tasks.
The result is the last line of output, as JSON.
"""
import json
import sys

import requests

from desktop_env.envs.desktop_env import DesktopEnv


def main():
    mode, domain, task_id = sys.argv[1:4]
    with open(f"evaluation_examples_windows/examples/{domain}/{task_id}.json") as f:
        task = json.load(f)
    env = DesktopEnv(action_space="pyautogui", require_a11y_tree=False, emulator_ip="20.20.20.21")
    if mode == "reset":
        # WAA's local mode only closes windows between tasks (its Azure runs revert the VM). A LibreOffice "Save
        # changes?" prompt left by one task freezes every LibreOffice window in the next, and "Pick an app" dialogs
        # hold the foreground, so close both first, for every tool layer alike. Killing LibreOffice leaves it a
        # recovery list, which made the next start open "Document Recovery" (in a fifth of round 4's tasks), so its
        # backups and recovery entries go too. A Windows Firewall prompt ("allow ... to access networks?", PickerHost)
        # raised by a VS Code task held the foreground through every later task on that VM (round 7); closing it is
        # Cancel: the firewall keeps blocking.
        requests.post("http://20.20.20.21:5000/setup/execute", timeout=60, json={"shell": False, "command": [
            "powershell", "-NoProfile", "-Command",
            "taskkill /f /im soffice.bin /im soffice.exe /im OpenWith.exe /im PickerHost.exe 2>&1 | Out-Null; Start-Sleep 1; "
            "$u = \"$env:APPDATA\\LibreOffice\\4\\user\"; Remove-Item \"$u\\backup\\*\" -Recurse -Force -ErrorAction SilentlyContinue; "
            "$x = \"$u\\registrymodifications.xcu\"; if (Test-Path $x) { "
            "[IO.File]::WriteAllLines($x, [string[]](Get-Content $x | Where-Object { $_ -notmatch 'RecoveryList' })) }"]})
        env.reset(task_config=task)
        print(json.dumps({"instruction": task["instruction"]}))
    else:
        env._set_task_info(task)
        env.setup_controller.reset_cache_dir(env.cache_dir)
        env.action_history = ["FAIL"] if "--fail" in sys.argv else []
        print(json.dumps({"score": float(env.evaluate())}))


if __name__ == "__main__":
    main()
