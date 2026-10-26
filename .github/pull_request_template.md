## What this changes

<!-- What changed and why. Link the issue it fixes, if there is one ("Fixes #12"). -->

## How you tested it

<!--
Windows version, the apps, and the MCP client (on the PC or over SSH).
For an engine change: the latency benchmark and, if you could run it, the agent task suite, before and after.
-->

## Checks

- [ ] `build\build.ps1` builds it (C# 5: no `$"..."`, `?.`, `=>` members or exception filters).
- [ ] I ran `oriel call windows` and `oriel call tree --Window <something>` on a real desktop.
- [ ] A speed claim comes with a measurement.
- [ ] A safety change (keys, the mouse, passwords, the deny-list, the daemon) comes with a test case that tries to get around it.
- [ ] The docs say what changed: `README.md`, `docs/tools.md`, `SECURITY.md` if it changes what oriel guarantees, and `CHANGELOG.md` under the next version.
- [ ] My contribution is under the [Apache License 2.0](../LICENSE), as [CONTRIBUTING.md](../CONTRIBUTING.md#licence-of-contributions) says.
