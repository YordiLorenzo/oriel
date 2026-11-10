# Roadmap

What comes next for oriel, and what stays out. Plans can change; to suggest something, open an issue or start a
thread in [Discussions](https://github.com/YordiLorenzo/oriel/discussions/categories/ideas).

## Next

1. **Code signing.** Releases are unsigned so far, so Windows warns about them, and Smart App Control, where it is on,
   blocks them. After the launch, at the end of November 2026, oriel applies to SignPath Foundation, which signs open
   source projects for free, so that Windows names the publisher. The
   [code signing policy](docs/code-signing.md) says what will be signed and who approves it.
2. **Windows 10.** oriel is meant to run on Windows 10 1903 and later, but has only been tested on Windows 11. Testing
   on Windows 10 22H2 comes first.
3. **More MCP clients, tested.** Most testing used Claude Code. Claude Desktop, through the `.mcpb` bundle that
   releases carry from 0.1.1, and the Codex CLI come next, then Cursor, VS Code and others. Each gets "tested" in the
   README only once it passes.
4. **LibreOffice.** In recent WindowsAgentArena rounds, Cua Driver completed more LibreOffice Writer and Calc tasks
   than oriel ([bench/waa/RESULTS.md](bench/waa/RESULTS.md)). Fixes have to help on any PC, not just the benchmark
   ([bench/waa/README.md](bench/waa/README.md#not-tuning-to-the-benchmark)).
5. **winget and Scoop.** The manifests are ready in [packaging/](packaging/) but not submitted yet, so neither works
   today. They go out after the launch. Until then, download `oriel.exe` from the
   [releases](https://github.com/YordiLorenzo/oriel/releases).
6. **oriel-vnc on PyPI,** so it installs with `uvx oriel-vnc` instead of a git URL. The release workflow is ready
   ([vnc-release.yml](.github/workflows/vnc-release.yml)) and publishes it with the first `vnc-v` tag. Until then, use
   the git URL in [vnc/README.md](vnc/README.md).

## Later

- Tested on more setups: high-DPI scaling, several monitors, a standard (non-admin) Windows account.
- Keys and Store apps in the head-to-head benchmark. The run of 2026-11-10 was on an unlocked PC, but it repeated the
  tasks of 2026-10-21, which use neither.

## Not planned

- **A shell or file tools by default.** oriel works through windows. `{"shell": true}` in `config.json` is there for
  people who want it.
- **Screenshots as the main way to see.** `screenshot` stays for canvases, charts and games.
- **Native macOS or Linux support in oriel.exe.** It is built on Windows UI Automation. Other systems are reachable
  through oriel-vnc, with screenshots.
- **Remote access without SSH.** The daemon listens on loopback only; remote use goes through SSH.
- **A Glama listing, for now.** Glama checks each server by starting it in a Linux container. oriel.exe needs Windows
  to start: under Mono and under Wine it exits before it reads a request (tested 2026-11-10). oriel gets listed there
  only if Glama offers a way for Windows-only servers.
