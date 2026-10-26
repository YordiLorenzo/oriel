# Roadmap

What comes next for oriel, and what stays out. Plans can change; to suggest something, open an issue.

## Next

1. **Code signing.** 0.1.0 is unsigned, so Windows warns about it. The plan is to apply to the SignPath Foundation,
   which signs open source projects for free, so Windows names the publisher.
2. **winget and Scoop.** Install and update with one command.
3. **Windows 10.** oriel is meant to run on Windows 10 1903 and later, but has only been tested on Windows 11. Testing
   on Windows 10 22H2 comes first.
4. **oriel-vnc on PyPI,** so it installs with `uvx oriel-vnc` instead of a git URL.
5. **More MCP clients, tested.** Most testing used Claude Code. Codex, Cursor, VS Code and others get a tested config
   in the README.
6. **LibreOffice.** In recent WindowsAgentArena rounds, Cua Driver completed more LibreOffice Writer and Calc tasks
   than oriel ([bench/waa/RESULTS.md](bench/waa/RESULTS.md)). Fixes have to help on any PC, not just the benchmark
   ([bench/waa/README.md](bench/waa/README.md#not-tuning-to-the-benchmark)).

## Later

- Tested on more setups: high-DPI scaling, several monitors, a standard (non-admin) Windows account.
- An unlocked run of the head-to-head benchmark, so keys and Store apps are measured too.

## Not planned

- **A shell or file tools by default.** oriel works through windows. `{"shell": true}` in `config.json` is there for
  people who want it.
- **Screenshots as the main way to see.** `screenshot` stays for canvases, charts and games.
- **Native macOS or Linux support in oriel.exe.** It is built on Windows UI Automation. Other systems are reachable
  through oriel-vnc, with screenshots.
- **Remote access without SSH.** The daemon listens on loopback only; remote use goes through SSH.
