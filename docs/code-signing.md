# Code signing policy

**Status, 11 November 2026:** SignPath Foundation has not accepted oriel yet, so releases up to 0.1.1 are unsigned.
This policy applies from the first signed release.

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org)

## What gets signed

- **`oriel.exe` from each [GitHub release](https://github.com/YordiLorenzo/oriel/releases), and nothing else.**
- The release workflow, [release.yml](../.github/workflows/release.yml), builds it from the version tag on a
  GitHub-hosted Windows runner. SignPath checks that the file came from that workflow run before it signs it.
- The signing policy accepts the file only when its product name is `oriel` and its product version is the release
  version (for example `0.2.0`).
- The approver approves each release's signing request by hand.
- `oriel.exe` holds no code from other projects. The UI Automation interop types inside it are generated during the
  build from Windows' own `UIAutomationCore.dll`.
- Not signed: oriel-vnc (Python source, no binary), the test builds of [build.yml](../.github/workflows/build.yml),
  and builds you make yourself.

To check a download, in PowerShell:

```powershell
Get-AuthenticodeSignature .\oriel.exe | Format-List Status, SignerCertificate
(Get-FileHash .\oriel.exe).Hash
```

`Status` should be `Valid`, the signer `CN=SignPath Foundation`, and the hash the one in the release's `SHA256SUMS`.

## Team roles

oriel has one maintainer, who holds every role.

| Role | What it covers | Members |
|---|---|---|
| Authors | Change the source without another review | [YordiLorenzo](https://github.com/YordiLorenzo) |
| Reviewers | Review every pull request from someone without write access before it is merged | [YordiLorenzo](https://github.com/YordiLorenzo) |
| Approvers | Approve each signing request in SignPath | [YordiLorenzo](https://github.com/YordiLorenzo) |

Everyone with one of these roles uses multi-factor authentication on GitHub and on SignPath. Reviews look hardest at
`build/build.ps1` and `.github/workflows/`, because they decide what gets built and signed.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it.

- `oriel.exe` connects only to its own daemon on `127.0.0.1`. It has no telemetry and no update checks.
- oriel works inside an MCP client that you choose, such as Claude Code or Codex. That client sends what oriel reads
  (window titles, control names, text) to its model provider, under the client's and the provider's privacy
  policies.
- oriel-vnc connects only to the VNC servers that you list in its `hosts.json`.

## Changes to your PC

[What oriel changes on your PC](../README.md#what-oriel-changes-on-your-pc) lists every file, registry value and
scheduled task, and `oriel uninstall` removes them.

## Reporting a problem

- A security problem in oriel: follow [SECURITY.md](../SECURITY.md).
- A signed file that you think breaks this policy or
  [SignPath Foundation's conditions](https://signpath.org/terms): write to SignPath at
  [support@signpath.io](mailto:support@signpath.io).
