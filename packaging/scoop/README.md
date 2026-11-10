# scoop-bucket

A [Scoop](https://scoop.sh) bucket for [oriel](https://github.com/YordiLorenzo/oriel), the MCP server that lets an AI
assistant use the apps on your Windows PC by reading each window as text, through the accessibility tree.

## Install

```powershell
scoop bucket add yordilorenzo https://github.com/YordiLorenzo/scoop-bucket
scoop install yordilorenzo/oriel
```

Scoop puts `oriel` on your PATH. Add it to Claude Code:

```powershell
claude mcp add --scope user oriel -- oriel mcp
```

Other clients and use from a Mac or Linux machine over SSH:
[Set up your MCP client](https://github.com/YordiLorenzo/oriel#set-up-your-mcp-client).

## Update

```powershell
scoop update oriel
```

## Uninstall

```powershell
oriel uninstall
scoop uninstall oriel
```

`oriel uninstall` stops oriel and deletes its settings folder, `%LOCALAPPDATA%\oriel` (the per-user key and
`config.json`). Scoop doesn't remove that folder by itself.

## Manifests

| Manifest | Source | Checksum |
|---|---|---|
| `bucket/oriel.json` | `oriel.exe` from the [GitHub releases](https://github.com/YordiLorenzo/oriel/releases) | SHA256 from each release's `SHA256SUMS` |

The Excavator workflow runs Scoop's `checkver` and `autoupdate` on a schedule. When a new oriel release is out, it
updates the version, URL and hash in `bucket/oriel.json` and commits them.
