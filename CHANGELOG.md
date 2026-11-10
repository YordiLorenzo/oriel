# Changelog

All notable changes to oriel are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## 0.1.1 - unreleased

New ways to install and to check a download. The tools work as in 0.1.0.

### Added

- **Claude Code plugin:** `claude plugin install oriel --marketplace YordiLorenzo/oriel` adds oriel to Claude Code on Windows (Claude Code 2.1.292 or later). It starts the `oriel.exe` that `oriel install` put in `%LOCALAPPDATA%\Programs\oriel`, so run `oriel install` first.
- **Claude Desktop bundle:** each release has `oriel-<version>-win-x64.mcpb`, an MCP bundle for Claude Desktop, with its hash in `SHA256SUMS`. Open it with Claude Desktop to add oriel as an extension. It isn't tested in Claude Desktop yet.
- **MCP Registry:** each published release is registered in the official MCP Registry as `io.github.YordiLorenzo/oriel`, titled "oriel for Windows". `io.github.ParadoxInfinite/oriel` is a different project.
- **Build provenance:** `oriel.exe` and the bundle carry a GitHub build provenance attestation. `gh attestation verify oriel.exe -R YordiLorenzo/oriel` checks that this repository's release workflow built the file.
- **`oriel install` prints your config:** it ends with ready-to-paste config for your real path: the `claude mcp add` line, the `mcpServers` JSON for Claude Desktop and Cursor, the TOML for Codex and the SSH line for a Mac or Linux machine (a `'` in the user name or path is quoted for that shell), then a link to Discussions to say whether it worked.
- **oriel-vnc release workflow:** a `vnc-v*` tag checks the version, builds oriel-vnc, publishes it to PyPI through trusted publishing and lists it in the MCP Registry as `io.github.YordiLorenzo/oriel-vnc`. The package has PyPI metadata, and its README uses absolute links so they work on PyPI. It isn't on PyPI yet.

### Changed

- The exe's CompanyName and LegalCopyright properties are filled in. They were empty.
- The release workflow starts only on `v<digit>` tags, so a `vnc-v*` tag no longer starts the `oriel.exe` release.

### Docs, benchmarks and repository

- **README:** opens with the gap it fills (the Claude Code CLI has no computer use on Windows), then install with the plugin from 0.1.1, two first prompts that act, every Windows warning including Smart App Control, round 9 with its caveats and the per-call table of 10 Nov 2026. SECURITY.md, CONTRIBUTING.md and ROADMAP.md follow it.
- **Code signing policy:** [docs/code-signing.md](docs/code-signing.md) says what will be signed, who approves it and what oriel sends over the network, for the SignPath Foundation application.
- **WindowsAgentArena:** `bench/waa/stats.py` gives bootstrap 95% intervals for round 9 and compares each layer's own two runs. Cua Driver's two runs differed by 6.5 points, more than its 4.2-point lead. RESULTS.md and docs/benchmarks.md state Cua Driver's version, the 4 of its 63 tools that were hidden and that cost is Claude Code's list-price estimate. docs/benchmarks.md adds a "What this does not show" list and every round in one table.
- **Head-to-head, 10 Nov 2026:** oriel 0.1.0 against Cua Driver 0.46.1 on an unlocked PC, with a same-day 0.38.0 control. oriel took less time on every call, by less than on 21 Oct (list windows 1 ms against 30 ms). The harness fetches a chosen Cua Driver version, starts only the tools asked for, turns Cua's telemetry and update check off and adds a direct-mode Cua relay.
- **winget and Scoop manifests** for oriel 0.1.0 in `packaging/`, not submitted yet. The Scoop uninstall runs `oriel.exe` from its app folder, because through the Scoop shim `oriel uninstall` stops itself.
- **Issue forms:** a new form for an app oriel can't read (it asks for the window list and the tree), Claude Desktop and VS Code in the bug report's client list, questions sent to Discussions, and a note that `oriel` in the commands means the full path, because install doesn't add it to PATH.
- **Code of conduct:** Contributor Covenant 3.0, with reports through the same private form as security problems.
- **Preview card:** says Windows 11 and "Lets Claude Code use your Windows apps." instead of Windows 10 and 11 and the old tagline. Its HTML source is in `docs/images/source`.
- **Download page:** `site/download/` sends visitors on to the latest `oriel.exe` and can count the click with GoatCounter, which sets no cookies.
- **Demo kit** in `demo/`: a small Windows Forms sign-up app, the prompt and session settings for Claude Code, a screen recording script, and a trace check that fails on any `keys`, `click`, `drag`, window or shell call.

## 0.1.0 - 2026-10-26

First release.

### Added

- A single `oriel.exe` (C# 5 on the .NET Framework 4.8, UI Automation interop embedded) with five modes: `mcp`, `serve`, `call`, `install` and `uninstall`.
- **oriel-vnc** (`vnc/`): a second MCP server, in Python, that operates any desktop with a VNC server (macOS, Linux, Windows) through screenshots scaled for the model, clicks in screenshot coordinates, keys, typing and a zoom for small text. One connection per machine, logins from an environment variable or a command so the host list holds no passwords. Install with `uvx --from "git+https://github.com/YordiLorenzo/oriel#subdirectory=vnc" oriel-vnc`.
- MCP tools: `status`, `windows`, `tree`, `find`, `text`, `wait`, `screenshot`, `invoke`, `set`, `select`, `scroll`, `keys`, `click`, `drag`, `batch`, `launch`, `focus`, `window`, `close`.
- **Speed:**
  - AutoSetFocus off;
  - one cross-process call per window read;
  - provider-side name search;
  - change reports settled by WinEvents;
  - stable refs, a name index and a 1 ms timer.
- **Safety:**
  - per-user token, and non-JSON connections dropped;
  - password fields refused;
  - deny-list for sign-in, UAC and password managers, and an optional allow-list;
  - no shell by default: `launch` refuses command interpreters and open terminal windows are off limits; `"shell": true` in `config.json` lifts both;
  - checked, ordered keys.
- **From the WindowsAgentArena runs:** bringing a window to the front from the daemon now works (its thread had no message queue, so attaching to the foreground thread failed); `keys` falls back to a Win32 focus check when an app's UIA provider hangs; `wait` can wait for a window or just pause; `screenshot` defaults to the window in front and says how its pixels map to window pixels; `text` falls back when a TextPattern fails.
- **One model turn fewer per session:** the tools carry `anthropic/alwaysLoad`, so Claude Code loads them up front instead of behind a tool search.
- **Faster actions on slow machines:** a lookup by name in a window not read in the last 120 s walks it once and keeps that walk for the change report (it used to search, then walk); the focus check before `keys` is one cross-process call instead of four, and nothing is asked of the app after typing (it waited for the app to handle every key).
- **Notification toasts no longer block keys:** a toast (ShellExperienceHost) can keep the foreground after it has shown, and Windows then refuses to bring any other window to the front. oriel now presses Alt+Tab once, so the system itself switches windows, and tries again. Other windows that hold the foreground, such as security prompts, are still left alone.
- **Fewer refused calls:** `keys` types a parenthesis or bracket that no modifier opens as itself (`=SUM(A1:A3)` used to be refused until escaped), and a `window` that matches nothing lists the open windows in the error instead of asking for a `windows` call.
- **Remote use:** `ssh pc oriel mcp`. Works around the .NET Framework stdin bug under Windows OpenSSH.
- **Install:** `oriel install` copies the exe to `%LOCALAPPDATA%\Programs\oriel` and starts the daemon, for your Windows account only and without admin rights. It starts at every logon only if you ask for it with `oriel install --autostart` (an HKCU Run entry).
- **Uninstall:** `oriel uninstall` stops the daemon and removes the logon entry, `%LOCALAPPDATA%\Programs\oriel` and `%LOCALAPPDATA%\oriel` (token, port, `config.json`), and lists what it removed.
- **Releases:** `oriel version` and the exe's file version come from the release tag. Each GitHub release has `oriel.exe` and `SHA256SUMS`.
