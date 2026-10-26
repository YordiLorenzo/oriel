# Security policy

oriel lets an AI assistant operate a real Windows desktop. This page says how to report a problem, what oriel
guarantees, and where those guarantees stop.

## Supported versions

Security fixes go into the latest release only.

## Reporting a vulnerability

Please don't open a public issue for a security problem. Report it privately through GitHub instead: on the
repository's **Security** tab, choose **Report a vulnerability**, or go straight to
<https://github.com/YordiLorenzo/oriel/security/advisories/new>.

A useful report says:

- the oriel version (`oriel version`) and the Windows version (`winver`);
- what an attacker needs: another program running as the same account, another Windows account on the PC, a web
  page or document the assistant reads, or something on the network;
- the steps, or a proof of concept;
- what it lets the attacker do.

Expect a first answer within a week. oriel is maintained in spare time, so a fix can take longer, but you'll hear
what happens next and when the fix ships. You'll be credited in the advisory unless you'd rather not be.

### Worth reporting

For example:

- a way for another program, a web page or another Windows account to send requests to the daemon, or read its
  replies, without the per-user key;
- a way to read, fill or type into a password field, or to act on an off-limits window;
- a way to make `keys`, `click` or `drag` land in a window other than the one oriel verified, or to get text past the
  rule that `{TAB}secret` is refused;
- `launch` starting a command interpreter or script host, or keys reaching an open terminal, while `shell` is off;
- text from a window or a web page being treated as oriel's own instructions to the assistant rather than as a tool
  result.

Not in scope: an assistant that misreads a task or follows instructions it found on a page, and then acts in a window
it is allowed to use (oriel limits where it can act, not what it decides; a way past those limits is in scope);
Windows' warnings about unsigned builds; and problems in Windows, UI Automation or the apps themselves. Please report
those to their makers.

## Threat model

The assistant is **trusted to act but not to be perfect**: it may misread a task, or follow instructions it found on
a web page or in a document (prompt injection). oriel limits what such a mistake can reach.

Other programs and web pages on the same PC are **not trusted**.

## What oriel does

- **No shell by default, no files.** No tool runs commands or reads or writes files.
  - `launch` starts a program, document or URI, as double-clicking it would. It refuses command interpreters and
    script hosts (cmd, PowerShell, Windows Script Host and similar).
  - Terminals that are already open are off limits too (Windows Terminal, console windows of cmd and PowerShell, the
    PowerShell ISE, mintty and similar), because keys typed into one run as commands. In a WindowsAgentArena run,
    agents typed PowerShell into an open terminal once `launch` refused to start one.
  - To give the assistant a shell, put `{"shell": true}` in `config.json`. The deny list and an allow-list still
    apply.
  - With an allow-list, `launch` starts only listed programs.
- **Passwords.** Password fields (UIA `IsPassword`) can't be filled, read, or typed into. `keys` refuses when the
  focused element is a password field.
- **Off-limits windows.** The lock screen (LockApp), sign-in (LogonUI), UAC prompts (consent), Windows credential
  dialogs (CredentialUIBroker), and common password managers (1Password, KeePass, KeePassXC, Bitwarden, Dashlane,
  LastPass, RoboForm, Enpass, NordPass, Keeper) can't be read or acted on.
  - Add your own in `%LOCALAPPDATA%\oriel\config.json`: `{"deny": ["MyBankApp"]}`.
  - Or allow only some processes: `{"allow": ["notepad", "chrome"]}`.
  - Restart the daemon after editing.
- **Element actions in the background.** `invoke`, `set`, `select` and `scroll` use UI Automation patterns, which
  don't move the mouse or take keyboard focus, so they don't interfere with what the user is doing.
- **Keys.** They are sent only after checking that the target window is in front, the focus is inside it, and it's
  not a password field. The one key sent before that check is Alt+Tab, to the system's window switcher, when a
  notification toast holds the foreground and Windows refuses to bring the target forward. A call may hold text only
  before its first key that can move focus, so `{TAB}secret` can't land in the next field. See
  [docs/design.md](docs/design.md#6-keys-done-safely).
- **The mouse.** `click` and `drag` send real mouse input (for cells, canvases and drawings that have no UIA action)
  with the same checks as keys: refused while the PC is locked or the user is active, the window brought to the front
  and verified, and the point inside that window and not covered by another one.
- **The daemon:**
  - It listens on 127.0.0.1 only, on a port of its own per Windows user (`oriel port`).
  - Every connection starts with a handshake in which client and daemon each prove they hold the per-user key in
    `%LOCALAPPDATA%\oriel\token` (HMAC challenge and response; the key itself is never sent). Only your own clients
    can make requests, and they only talk to your own daemon.
  - A connection that sends anything but a JSON object is closed at once, so a web page can't reach it through the
    browser.
  - If a connection breaks after a request was sent, the client reports that the outcome is unknown rather than
    sending it again.
- **Remote use** goes through SSH, so authentication and encryption are SSH's. `oriel mcp` in an SSH session talks to
  the daemon over loopback, through the handshake.
- **No network.** oriel makes no connections other than to its own daemon on 127.0.0.1. No telemetry, no update
  checks.

## Limits

- **The assistant acts as you.** Anything you can do in an allowed window, it can do: send an email, delete a file in
  Explorer (to the Recycle Bin), change a setting. Use the allow-list to keep it to the apps a task needs.
- **Keys and clicks are riskier** than element actions. Prefer `set` and `invoke`; `keys`, `click` and `drag` are
  refused while the user is typing or moving the mouse, and while the PC is locked.
- **`screenshot`** shows whatever the window shows.
- **What oriel reads goes to your model provider.** Window titles, control names and text the assistant reads are
  sent by your MCP client to its model, like anything else you show it.
- **Elevated windows** (running as administrator) are out of reach unless oriel itself runs elevated. Don't run it
  elevated without a reason.

## oriel-vnc

oriel-vnc runs on the computer where the assistant runs and can do anything a person at the remote screen could,
including on its lock screen. It has no deny-list: it sees pixels, not windows. Point it only at machines you own,
keep their VNC servers on your own network or tailnet, and give logins as `env:` or `cmd:` values rather than
plain text in `hosts.json`. Reports about oriel-vnc follow the same process as above.

## Downloads

Release builds aren't code-signed yet, so Windows may show a SmartScreen warning. Download oriel only from this
repository's [Releases page](https://github.com/YordiLorenzo/oriel/releases), and compare the file's hash with the
release's `SHA256SUMS` (`Get-FileHash .\oriel.exe`). Or build it yourself: the build needs nothing beyond Windows.
What oriel changes on your PC is listed in the
[README](README.md#what-oriel-changes-on-your-pc).
