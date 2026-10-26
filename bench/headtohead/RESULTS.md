# Windows computer-use head-to-head, 2026-10-21

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

## Reading it

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

## Still to do

- **An unlocked run**, so Windows-MCP's click and type count and keys can be compared.
- **A Store app (Calculator)**, which the locked PC suspends.
- Done since: **the same model with only the tool layer swapped**, on WindowsAgentArena, to measure turns and completed tasks as well as milliseconds. See `bench/waa/`.
