# Changelog

All notable changes to oriel are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

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
