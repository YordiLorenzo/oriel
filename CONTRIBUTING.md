# Contributing to oriel

Thanks for helping. Bug reports, apps oriel can't read or act on, configs for more MCP clients, and fixes of any size
are welcome. For a large change, please open an issue first so we can agree on the approach before you spend the
time.

Be kind to each other. The maintainer moderates issues and pull requests.

## Reporting a bug

Open an issue with the bug report form. It asks for the oriel and Windows versions, your MCP client and whether it
runs on the PC or over SSH, the app involved, and what happened. The most useful part is the tool call and oriel's
reply: `oriel call` reproduces most of them from a terminal, for example
`oriel call tree --Window "Character Map"`. Replies hold window titles and text from your screen, so read them before
you post one.

Security problems go through a private report, as [SECURITY.md](SECURITY.md) describes, not a public issue.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File build\build.ps1    # -> dist\oriel.exe
```

No SDK needed: the build uses the `csc.exe` that ships with the .NET Framework. That means the code is **C# 5**: no
`$"..."`, no `?.`, no `=>` members, no exception filters. CI builds every push on `windows-latest`.

## Layout

- `src/Engine.cs` holds UI Automation, the tree, actions and change reports, and the daemon (`Engine.Serve`).
- `src/Mcp.cs` is the MCP stdio server: tool table, validation, JSON-RPC.
- `src/Program.cs` holds the command line, the daemon client used from SSH sessions, and install.
- `bench/` holds the latency benchmark, the head-to-head harness against other tools, the agent task suite and the
  WindowsAgentArena harness. [bench/README.md](bench/README.md) says how to run each.

## Before a pull request

1. Build, then run `oriel call windows` and `oriel call tree --Window <something>` on a real desktop.
2. When you touch the engine, run the latency benchmark (`bench/latency.py`) and, if you can, the agent task suite
   (`bench/tasks/run.py`), and say in the pull request what changed. The suite needs a Windows PC reachable over SSH
   and costs model usage; if you can't run it, say so and the maintainer will.
3. Speed claims need a measurement.
4. **Safety changes** (keys, the mouse, passwords, the deny-list, the daemon's front door) get a test case that tries
   to get around them.
5. Keep comments short, and say why rather than what: the Windows behaviour, the app quirk or the failure the code is
   there for.

## Commits and pull requests

- One topic per commit. The first line says what changes, in plain words, in about 72 characters; the body says why,
  when that isn't obvious.
- In the pull request, say what changed and how you tested it: the Windows version, the apps, and the MCP client.
- Update the docs in the same pull request: `README.md` and `docs/tools.md` for what tools do, `SECURITY.md` for
  anything that changes what oriel guarantees, and `CHANGELOG.md` under the next version.

### Licence of contributions

oriel is published under the [Apache License 2.0](LICENSE). As section 5 of that licence says, a contribution you send
is under the same licence, unless you state otherwise. Send only work that is your own, or that you have the right to
submit.
