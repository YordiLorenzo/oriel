# oriel for Windows

oriel is an MCP server that lets Claude Code use the apps on your Windows PC. It reads each window as text through
the Windows accessibility tree (UI Automation), acts on the window's controls, and replies to every action with what
changed in the window: `+` new, `~` changed, `-` gone.

- Press, fill, select and scroll go through UI Automation, so they don't move your mouse or take the focus. Keys,
  click and drag use the real keyboard and mouse, for controls that have no accessible action.
- Password fields, sign-in and UAC prompts, credential dialogs, common password managers and open terminals are off
  limits. There is no shell or file tool by default. It is not a sandbox: in an allowed window it acts as you.
- Tools: `status`, `windows`, `tree`, `find`, `text`, `wait`, `screenshot`, `invoke`, `set`, `select`, `scroll`,
  `keys`, `click`, `drag`, `launch`, `window`, `focus`, `close`, `batch`.

## Requirements

- Windows 11. Windows 10 version 1903 or later should work, but isn't tested yet.
- Claude Code on that Windows PC. The plugin starts a program on your PC, so claude.ai chat skips it, and it isn't
  tested in Cowork.
- Git for Windows, to install the plugin from its GitHub marketplace.

## Install

In PowerShell, with Claude Code 2.1.292 or later:

```powershell
claude plugin install oriel --marketplace YordiLorenzo/oriel
```

Then start `claude` and try one of these:

- Which windows are open?
- Open Calculator and work out 127 times 46 by pressing its buttons.
- Open Notepad, write a three-line note and save it in Documents as oriel-test.txt.

## What the plugin runs

The plugin holds one program, `oriel.exe` (under 200 KB), and Claude Code starts it as `oriel.exe mcp`. It runs on
the .NET Framework 4.8 that Windows includes and needs no admin rights.

This `oriel.exe` is the asset of the [GitHub release](https://github.com/YordiLorenzo/oriel/releases) with the same
version as the plugin. The repository's `plugin.yml` workflow copies it in after it checks the file against the
release's `SHA256SUMS` and its build provenance. To check your copy, compare `(Get-FileHash oriel.exe).Hash` with
`SHA256SUMS`, or run `gh attestation verify oriel.exe -R YordiLorenzo/oriel`. The source is in the repository's `src`
folder, and it builds with the C# compiler that Windows includes.

## What it sends, fetches and changes

- oriel makes no network connections except to itself on `127.0.0.1`. It has no telemetry and no update check.
  Claude Code sends what oriel reads (window titles, control names, the text it reads) to its model provider, as it
  does with anything else you show it.
- It keeps a per-user key, a counter for refs and an optional `config.json` in `%LOCALAPPDATA%\oriel`.
- When Claude Code runs where there is no desktop, such as an SSH login, oriel starts a copy of itself in your
  desktop session through a one-time scheduled task, `oriel-start`, which is deleted once it has started. That copy
  listens on `127.0.0.1` for clients that hold your per-user key, and runs until you sign out, also after the plugin
  is updated or uninstalled. In Claude Code on the desktop itself, oriel runs inside its own MCP process and starts no
  other copy.

## Windows warnings

oriel isn't code-signed yet. Smart App Control, where it is on, blocks it. Antivirus may be wary of an unsigned
program that sends keystrokes and reads other windows.

## Uninstall

Run `claude plugin uninstall oriel@oriel`. To remove oriel's settings too, stop any copy that still runs
(`Stop-Process -Name oriel` in PowerShell), then delete the `%LOCALAPPDATA%\oriel` folder.

## More

Other MCP clients, the install without the plugin, benchmarks and the security rules are in the
[full README](https://github.com/YordiLorenzo/oriel#readme). Did it work? Say so in
[Discussions](https://github.com/YordiLorenzo/oriel/discussions/6).

Licence: Apache-2.0.
