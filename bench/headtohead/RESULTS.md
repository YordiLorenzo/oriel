# Windows computer-use head-to-head

## 2026-11-10, second run: agent-desktop 1.2.0 added

The same tasks, PC (TOASTER) and MacBook as the run below, a few hours later, with [agent-desktop 1.2.0](https://github.com/vercel-labs/agent-desktop/releases/tag/v1.2.0) added (vercel-labs, released 2026-11-05, the first release with an MCP server). oriel 0.1.0 and Cua Driver 0.46.1 ran again in the same session, so the three columns share one set of conditions. 5 runs per task; the table shows the median wall time per MCP call. Rows: `results-2026-11-10b.json`; printed tables: `results-2026-11-10b.txt`.

**What is different from the first 2026-11-10 run:**
- **agent-desktop 1.2.0:** the x86_64 Windows archive, SHA-256 checked against the release's `checksums.txt`; the exe is unsigned. It ran as `agent-desktop mcp` with no flags, the setup its [MCP guide](https://github.com/vercel-labs/agent-desktop/blob/v1.2.0/apps/docs/content/docs/guides/mcp.mdx) gives. Its actions are headless by default (UI Automation patterns, no pointer or focus change), and without a session it shows no cursor overlay. Its state folder was in the benchmark folder (`AGENT_DESKTOP_HOME`).
- **agent-desktop's settings:** the loop from its [quick start](https://github.com/vercel-labs/agent-desktop/blob/v1.2.0/README.md): `snapshot -i` (interactive elements, each with a ref), then `set-value` and `click` on refs. On Character Map a pilot gave 140 to 153 ms for the full tree, `-i` and `-i --compact` alike, so the table uses `-i`. Browser windows were read twice: with `-i`, and as the skeleton its docs give for dense windows (`-i --compact --skeleton`, [Skeleton and drill-down](https://github.com/vercel-labs/agent-desktop/blob/v1.2.0/apps/docs/content/docs/skeleton-and-drill-down.mdx)). `-i --max-depth 6` took 2.44 s in the pilot, no faster than the skeleton.
- **Browsers:** Chrome and Edge as guests, each in a new profile folder, on the same Wikipedia page (`browsers-start.ps1`). In the first run Edge signed its new profile in to the owner's Windows account by itself. This time it did the same within a minute with `--inprivate` and with `--guest`, so Edge also gets `--disable-features=msImplicitSignin`. After the run `browsers-check.ps1` counted 0 accounts in both bench profiles and 1 in each of the three Edge profiles that had signed in (the negative control), and the owner's own Edge `Local State` had the same hash and time before and after.
- **Cua Driver's daemon was warmed by one run first**, as the first run's pilot did. See "Not used" below.

| Task | oriel 0.1.0 | Cua Driver 0.46.1 | agent-desktop 1.2.0 |
|---|---|---|---|
| List windows | **2 ms** | 31 ms | 44 ms |
| Launch Character Map | **193 ms** (waits for the window) | 605 ms | 251 ms (named the window 5 of 5 times) |
| Launch + return its tree, one call | **294 ms** | 2 calls | 2 calls |
| Character Map tree | **21 ms** | 37 ms (+ 30 ms list_windows to find the window) | 143 ms |
| Fill a field, by ref/token | **2 ms** | 3 ms | 174 ms |
| Fill a field, by name | 15 ms | n/a | n/a (find, then act: not timed) |
| Press a button, by ref/token | **2 ms** | 6 ms | 187 ms |
| Press + report what changed | **44 ms** | needs a second get_window_state | needs a new snapshot (click lists the open windows only) |
| Chrome window tree | **101 ms**, 9.1 KB* | 218 ms, 25.9 KB* | 3,058 ms, 131.6 KB, 1 of 5 reads partial*; skeleton 2,402 ms, 16.4 KB |
| Edge window tree | **104 ms**, 1.3 KB* | 226 ms, 26.6 KB* | 3,066 ms, 137.6 KB, 4 of 5 reads partial*; skeleton 2,418 ms, 22.4 KB |
| Correct outcome (field = "abc" + char) | 10/10 | 5/5 | 5/5 |

\* Not like-for-like: each tool stops at its own default limit. oriel stops at depth 12 and 400 elements, which on Edge leaves out the page; Cua stops at 250 elements or depth 25; agent-desktop's snapshot stops at its 3 s observation deadline ([`--timeout-ms` 3000](https://github.com/vercel-labs/agent-desktop/blob/v1.2.0/apps/docs/content/docs/commands/observation.mdx)) and returns what it read, marked `complete: false`. agent-desktop answers in JSON, the other two in text.

**Reading it:**
- **oriel is fastest on every row.** Cua Driver is second on trees and actions.
- **agent-desktop launches faster than Cua Driver** (251 against 605 ms), but takes 143 to 187 ms per Character Map call, against 2 to 37 ms for the other two. This run did not measure where its time goes. Its docs list work each call does: the MCP server hands each call to the CLI ([MCP guide](https://github.com/vercel-labs/agent-desktop/blob/v1.2.0/apps/docs/content/docs/guides/mcp.mdx), "Tool arguments become CLI arguments"), every CLI call runs in its own process ([1.1.0 changelog](https://github.com/vercel-labs/agent-desktop/blob/v1.2.0/CHANGELOG.md)), a ref acts only when exactly one live element in the whole window still matches it ([Windows skill](https://github.com/vercel-labs/agent-desktop/blob/v1.2.0/skills/agent-desktop-windows/SKILL.md)), and `set-value` reads the value back. A call that does no UI work, `desktop_version`, took 13 ms in the pilot.
- **On the browser pages agent-desktop takes 2.4 to 3.1 s per tree.** `-i` alone took 3.0 to 3.1 s on every read: 5 of the 10 reads ran into the 3 s deadline and came back partial (`complete: false`), the other 5 finished just inside it. The skeleton shows 3 levels and still took 2.4 s; its docs say that off macOS it reads the whole window to check the refs ([Skeleton and drill-down](https://github.com/vercel-labs/agent-desktop/blob/v1.2.0/apps/docs/content/docs/skeleton-and-drill-down.mdx)).
- **Injected keys, source not known:** the run's input watcher counted 2 injected key events per agent-desktop Character Map run (10 in each 10 s window that held its 5 runs). A re-test the same evening, one agent-desktop call every 2 s through its CLI and through `agent-desktop mcp`, with each key event time-stamped, saw none from its launch, snapshot, `set-value` or `click`, while an injected F24 control key was seen. So this run does not show that agent-desktop sends keys.
- **Same-day check:** Cua Driver 0.46.1 is within 4 ms of the first 2026-11-10 run on every row. oriel is within 2 ms, except the browser trees (Chrome 101 against 109 ms, Edge 104 against 107 ms, in new guest windows on the same page).
- **Not used: the first combined run**, started 30 s after a fresh Cua Driver daemon. In it Cua Driver took 1.1 to 22 times as long per row (list windows 109 ms, fill 67 ms, press 90 ms), and agent-desktop's Character Map calls 90 to 115 ms longer. oriel, which ran before Cua's first call, matched. Then agent-desktop alone (Cua Driver stopped), oriel alone, Cua Driver alone after a warm-up run, and all three again gave the table above, with total CPU under 15% throughout. The cause is not known. All five sets are in the JSON.
- **Not run this round:** Terminator and Windows-MCP.

**How it ran:** `get-competitors.ps1 -Dir D:\agent-tools\hth-2026-11-10b -Tools oriel,cua,agent-desktop`; then in the desktop session `browsers-start.ps1`, `bench-start.ps1 -Tools cua`, `relay-start.ps1` (Cua daemon, port 3018) and `relay-start.ps1 -Tool agent-desktop` (port 3020), each with `-Dir D:\agent-tools\hth-2026-11-10b`; `browsers-check.ps1` with the same `-Dir` over SSH before and after. On the Mac, an SSH tunnel for 23018 and 23020, one warm-up run (`--runs 1 --tools cua`), then `ORIEL_BENCH_SSH=toaster ORIEL_BENCH_DIR='D:\agent-tools\hth-2026-11-10b' uv run --script headtohead.py --runs 5 --tools oriel,cua,agent-desktop`. A watcher in the desktop session saw no keyboard, mouse or controller input from a person.

## 2026-11-10: oriel 0.1.0 against Cua Driver 0.46.1

The 2026-10-21 tasks again, for oriel and Cua Driver only, on the same PC (TOASTER) and from the same MacBook on the LAN. 5 runs per task; the table shows the median wall time per MCP call. Rows: `results-2026-11-10.json`; printed tables: `results-2026-11-10.txt`.

**What is different from 2026-10-21:**
- **The PC was unlocked**, idle, with the game home screen in front. A watcher in the desktop session saw no keyboard, mouse or controller input from a person during the run.
- **Versions:** oriel 0.1.0 (the release exe) and Cua Driver 0.46.1 (newest on 2026-11-10). Both SHA-256 checked against their release checksums. Cua's telemetry and update check were off for the run's processes.
- **Browsers:** Chrome and Edge both showed one static page (Wikipedia, "Microsoft Windows"). The referee read each once before timing, because a browser builds a page's accessibility tree only when a client first asks.
- **Cua Driver ran two ways:** through its daemon (`serve --no-overlay`, then `mcp --socket`, as last round), and as bare `cua-driver mcp`, which on Windows now runs the driver in the MCP process. Bare `mcp` shows its agent cursor by default, which adds about 565 ms to each fill and press. With the cursor off (`set_agent_cursor_enabled`, untimed) it matches the daemon within 2 ms (fill 5 ms, press 7 ms). The table shows the daemon.

| Task | oriel 0.1.0 | Cua Driver 0.46.1 | Cua Driver 0.38.0, same day |
|---|---|---|---|
| List windows | **1 ms** | 30 ms | 40 ms |
| Launch Character Map | **192 ms** (waits for the window) | 604 ms | 619 ms |
| Launch + return its tree, one call | **294 ms** | 2 calls | 2 calls |
| Character Map tree | **20 ms** | 37 ms (+ 30 ms list_windows to find the window) | 40 ms |
| Fill a field, by ref/token | **2 ms** | 3 ms | 3 ms |
| Fill a field, by name | 15 ms | n/a | n/a |
| Press a button, by ref/token | **2 ms** | 5 ms | 7 ms |
| Press + report what changed | **42 ms** | needs a second get_window_state | same |
| Chrome window tree | **109 ms**, 12.3 KB* | 222 ms, 25.7 KB* | 222 ms |
| Edge window tree | **107 ms**, 1.5 KB* | 226 ms, 26.3 KB* | 228 ms |
| Correct outcome (field = "abc" + char) | 10/10 | 5/5 | 5/5 |

\* Not like-for-like: each tool stops at its own default limit. oriel stops at depth 12 and 400 elements, which on Edge leaves out the page; Cua stops at 250 elements ("PARTIAL TREE") or depth 25.

**Reading it:**
- **oriel is still faster on every row**, but by less than on 2026-10-21.
- **The old Cua gaps came from the conditions, not the version.** Cua 0.38.0, run in the same session, is within 15 ms of 0.46.1 on every row. So listing windows (501 ms then, 30-40 ms now) and launching (902 ms then, about 610 ms now) changed with the PC's state. The known difference is the lock (locked then, unlocked now); this is an inference, since the open windows differ too.
- **Per action both take a few ms** (oriel 2 ms, Cua 3-5 ms). The gaps left: listing windows (1 against 30 ms, and Cua needs it to find a window), launching (192 against 604 ms), and the one-call launch + tree and act + report that Cua has no match for.
- **Not run this round:** Terminator and Windows-MCP.

**How it ran:** `get-competitors.ps1 -Dir D:\agent-tools\hth-2026-11-10 -Tools oriel,cua`, then `bench-start.ps1 -Dir D:\agent-tools\hth-2026-11-10 -Tools cua`, `relay-start.ps1 -Dir D:\agent-tools\hth-2026-11-10` (daemon, port 3018) and `relay-start.ps1 -Dir D:\agent-tools\hth-2026-11-10 -Port 3019 -Direct` (bare mcp) in the desktop session; on the Mac, an SSH tunnel for 23018 and 23019, then `ORIEL_BENCH_SSH=toaster ORIEL_BENCH_DIR='D:\agent-tools\hth-2026-11-10' uv run --script headtohead.py --runs 5 --tools oriel,cua,cua-direct`. `headtohead.py` turns bare mcp's agent cursor off; the cursor-on rows came from the same command, run before that call was in the harness. The 0.38.0 control: the daemon and its relay stopped, then `bench-start.ps1 -Dir D:\agent-tools -Tools cua` and `relay-start.ps1 -Dir D:\agent-tools` (the 2026-10-21 copy), then `--tools cua`.

## 2026-10-21: four servers, PC locked

The same tasks through four MCP servers on one Windows 11 PC (build 26200, Ryzen 5 7600X), timed from a MacBook over the LAN (about 3 ms round trip). Every tool ran in the desktop session and every client ran on the Mac, the way an agent uses them. Harness: `headtohead.py`. Scripts for the PC: `get-competitors.ps1` (download and verify), `bench-start.ps1`, `relay-start.ps1` and `mcp-relay.ps1`.

**Tools tested:**
- **oriel**: native COM UIA, AutoSetFocus off, warm daemon. Measured as its engine stood on 2026-10-21, through its first MCP wrapper; `headtohead.py` now starts `oriel.exe mcp` over SSH.
- **Cua Driver 0.38.0**, trycua (released the same day).
- **Terminator MCP 0.24.28**, mediar-ai.
- **Windows-MCP 0.8.8**, CursorTouch.

**Condition:** the PC was **locked**. Windows-MCP's click and type send real input, which would land on the lock screen, so they were not run. Everything else acts through accessibility interfaces.

**Settings:** each tool used the fastest settings it offers: no screenshots, highlighting or post-action diffs. 5 runs per task, 3 for Windows-MCP. The table shows the median wall time per MCP call.

| Task | oriel | Cua Driver | Terminator | Windows-MCP |
|---|---|---|---|---|
| List windows | **6 ms** | 501 ms | 549 ms | in Snapshot |
| Launch Character Map | **203 ms** (waits for the window) | 902 ms | 2,384 ms | 1,182 ms |
| Launch + return its tree, one call | **298 ms** | 2 calls | 2 calls | 2 calls |
| Character Map tree | **23 ms** | 42 ms (+ ~500 ms list_windows to find the window) | 2,603 ms | 14,034 ms (whole-desktop snapshot) |
| Chrome window tree | **28 ms**, 6.4 KB | 48 ms, 9.2 KB | 2,684 ms, 9.1 KB | (snapshot) |
| Edge window tree (YouTube) | **38 ms**, 2.1 KB* | 65 ms, 19.5 KB | 2,768 ms, 30.7 KB | (snapshot) |
| Fill a field, by ref/token | **8 ms** | 11 ms | 4,698 ms (selector) | not run (locked) |
| Fill a field, by name | 19 ms | n/a | 4,698 ms | not run |
| Press a button, by ref/token | **8 ms** | 13 ms | 2,813 ms (selector) | not run (locked) |
| Press + report what changed | **45 ms** | needs a second get_window_state | ui_diff option (not timed) | needs a new snapshot |
| Correct outcome (field = "abc" + char) | 15/15 | 5/5 | 2/2 | n/a |

\* oriel's tree stops at depth 12 and 400 elements by default, so it shows less of a deep web page; Cua and Terminator walked deeper. The tree rows are comparable for Character Map and Chrome, not for Edge.

### Reading it

- **Cua Driver is the only close one.**
  - Per action it is within a few ms of oriel: 11–13 ms against 8 ms.
  - Trees: oriel is about 1.7× faster.
  - Big gaps: listing windows (6 vs 500 ms, and Cua needs that call to find a window's pid and id) and launching (203 vs 902 ms).
  - It has no one-call "act and report what changed" and no launch that returns the tree.
- **Terminator** takes 2.4–4.7 s per call, even without screenshots: about 100–500× slower.
- **Windows-MCP** needs 14 s to see the screen (a whole-desktop snapshot): about 600× slower than a window tree. Its launch took 1.2 s here, against 11 s measured earlier on the unlocked desktop.
- **Tool behaviour seen during the run:**
  - Terminator saves a window screenshot on every call by default (turned off here).
  - Cua Driver has telemetry on by default (turned off for this run).
  - Touching Character Map's field through Terminator made Windows report it as "RichEdit Control" afterwards (a different provider woke up), so name lookups for its label stopped matching.

### Still to do

- **An unlocked run**, so Windows-MCP's click and type count and keys can be compared.
- **A Store app (Calculator)**, which the locked PC suspends.
- Done since: **the same model with only the tool layer swapped**, on WindowsAgentArena, to measure turns and completed tasks as well as milliseconds. See `bench/waa/`.
