# oriel-vnc

oriel-vnc is the other half of [oriel](https://github.com/YordiLorenzo/oriel): an MCP server that lets an AI assistant
operate any computer that runs a VNC server, through screenshots, clicks and keys. Use it for the machines
`oriel.exe` can't run on (macOS, Linux) and for screens that have no accessibility tree, such as games, remote
desktops and canvases. On Windows apps, `oriel.exe` is faster and cheaper: it reads the window as text instead of
sending pictures.

oriel-vnc runs on the computer where the assistant runs and connects to each machine's VNC server. Nothing is
installed on the machines themselves.

## Set up

1. Turn on a VNC server on each machine: Screen Sharing on macOS (System Settings → General → Sharing), a VNC server
   such as TigerVNC or x11vnc on Linux, TightVNC or similar on Windows. Allow it only from your own network or
   tailnet.
2. List the machines in `~/.config/oriel-vnc/hosts.json` (or the file `$ORIEL_VNC_HOSTS` names). See
   [hosts.example.json](hosts.example.json):

   ```json
   {
     "mac": { "address": "192.168.1.20::5900", "username": "env:MAC_VNC_USER", "password": "env:MAC_VNC_PASSWORD" },
     "linux-box": { "address": "linux-box.local::5901", "password": "cmd:op read op://Private/VNC linux-box/password" }
   }
   ```

   - `address` is `host::port`.
   - `username` (macOS logins need one) and `password` can be the value itself, `env:NAME` to read an environment
     variable, or `cmd:...` to take the output of a command such as a password manager's CLI. Prefer the last two,
     so the file holds no passwords.
   - `encoding` is `zrle` (the default), `hextile` or `raw`. Use `raw` if screenshots time out against a TigerVNC
     server.
   - `cmd_key` is the keysym that `cmd` in `vnc_key` sends (default `super`).
3. Add it to your MCP client. With [uv](https://docs.astral.sh/uv/) installed:

   ```
   claude mcp add --scope user oriel-vnc -- uvx --from "git+https://github.com/YordiLorenzo/oriel#subdirectory=vnc" oriel-vnc
   ```

   Other clients: the command is `uvx` with the arguments `--from`,
   `git+https://github.com/YordiLorenzo/oriel#subdirectory=vnc` and `oriel-vnc`. From a checkout,
   `uv run --script vnc/oriel_vnc.py` works too.

Then ask the assistant to take a screenshot of one of your machines.

## Tools

| Tool | What it does |
|---|---|
| `vnc_hosts` | Lists the configured machines, their address and last seen screen size |
| `vnc_screenshot` | The whole screen, scaled down so its long edge is at most 1456 px |
| `vnc_zoom` | A region at full resolution, for small text |
| `vnc_click` | Click at x,y (left, right or middle; `clicks=2` for a double-click) |
| `vnc_move` | Move the pointer, for hover menus and tooltips |
| `vnc_drag` | Press, move in steps, release |
| `vnc_scroll` | Wheel clicks at x,y in any direction |
| `vnc_type` | Type text into the focused field |
| `vnc_key` | A key or combination: `enter`, `cmd+space`, `ctrl+alt+t`, `win+r` |

Every x,y is in the pixel space of the latest full screenshot of that machine; oriel-vnc scales it to the real
screen. Actions reply with a fresh screenshot about 0.6 s later, so the assistant sees what happened.

## How it behaves

- One VNC connection per machine, kept open between calls. A failed call drops the connection and retries once.
- A machine that is off or asleep fails within 5 seconds instead of waiting out VNC's 30-second timeout.
- Logins are read on first use and kept in memory only.

## Safety

oriel-vnc can do anything a person at that screen could, including on the lock screen. Point it only at machines
you own, keep VNC off the public internet, and don't let the assistant act on instructions it reads on screen.
oriel's [SECURITY.md](../SECURITY.md) covers how to report a problem.
