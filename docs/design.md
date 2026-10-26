# How oriel works, and why it's fast

oriel is a C# program on Windows' native UI Automation (UIA), the COM API that screen readers such as Narrator use. It runs as a warm daemon in the user's desktop session and speaks MCP over stdio, directly or through `ssh pc oriel mcp`.

Every number below was measured on a Windows 11 PC (build 26200, Ryzen 5 7600X), from a MacBook over a gigabit LAN, unless noted otherwise.

## 1. The 2-second stall: AutoSetFocus

UIA's action methods (`IUIAutomationInvokePattern::Invoke`, `IUIAutomationValuePattern::SetValue`, `IUIAutomationSelectionItemPattern::Select`, ...) first move keyboard focus to the element. It's documented, and on by default (`IUIAutomation2::AutoSetFocus`).

When Windows refuses to move focus (the workstation is locked, or another app such as a full-screen game holds the foreground), each call waits two seconds before acting:

| Call (PC locked) | AutoSetFocus on (default) | AutoSetFocus off |
|---|---|---|
| Chrome: press "Reload" | 2,024 ms | 8 ms |
| Chrome: set the address bar | 2,017 ms | 4 ms |
| Win32 dialog: set an edit field | 2,016 ms | 0 ms |
| Win32 dialog: press a button | 2,008 ms | 0 ms |

When focus *can* move, the default doesn't stall, but it steals focus from whatever the user is doing. oriel turns AutoSetFocus off, which needs `CUIAutomation8`; the older `CUIAutomation` can't do it.

We found no public report of the stall. Most agent tools keep the default (pywinauto even creates the old object, which can't turn it off).

**Before finding the cause,** oriel used MSAA (`IUIAutomationLegacyIAccessiblePattern::DoDefaultAction` / `SetValue`). MSAA takes 1–3 ms and never moves focus. It remains the fallback for elements that offer no UIA pattern but have a default action.

## 2. One cross-process call per window

**Trees:** reading a window is a single `BuildUpdatedCache` with `TreeScope_Subtree`. The cache request carries every printed property and a filter (control view AND not off screen), so a whole window (Chrome's toolbar plus page) crosses the process boundary in one call, and oriel walks the cached children locally.

**Name searches** (`find`, or acting by name) use `FindAll` with `PropertyConditionFlags_MatchSubstring`, evaluated inside the target app. Only the matches cross over.

**Windows** come from `EnumWindows` plus the DWM "cloaked" attribute (the Alt-Tab rule), not from UIA's desktop children, which took 1.3 s here because some windows answer slowly.

## 3. Change reports, settled by events

After `invoke`, `set`, `keys`, `select`, `scroll` or `batch`, oriel reports what changed in the window.

**When to look.** An out-of-context WinEvent hook (`SetWinEventHook`, object events, all processes, filtered to the target window) listens while the action runs. oriel reads the window once, as soon as the events have been quiet for 20 ms: about 25 ms after a typical click. If no event comes within 120 ms, it reads anyway: events only decide when to look, never what changed. Other tools sleep a fixed 200–1,500 ms.

**What changed.** The fresh read is diffed against the window's *last look*: the tree the agent saw, or the previous report.
- Elements are matched by UIA runtime id plus control type and automation id.
- Each line is `+` (new), `~` (changed name, value or state) or `-` (gone).
- More than 15 lines means the window changed a lot (a new page, another view). Then the reply is the new tree itself rather than a long diff.
- A window the action opened (a dialog) comes with its tree.

**Browsers.** Chrome starts loading a clicked link 100–200 ms later and exposes no busy state, so in browser windows oriel keeps listening (up to 1.5 s) when nothing has changed yet, or when the old page is gone but the new one hasn't arrived. A link click reports the new page in about 165 ms.

**Why it saves turns.** Without a report, an agent's next step after nearly every action is "look again", which costs a full model turn (seconds). Playwright MCP tried incremental snapshots and dropped them for complexity. oriel's diff is semantic (per element), settled by events, and falls back to a full tree when that's shorter.

## 4. Refs

Every element oriel shows gets a ref such as `kqm17`. `invoke`, `set` and `text` take a ref with no window, and act on the stored element directly, with no search.

- **Stability:** the same element keeps its ref across reads. It's found by runtime id, control type and automation id.
- **Retirement:** runtime ids can be reused once an element is gone, so a full read that no longer finds an element retires its ref, and a ref whose element turns out dead is retired when used. A ref never moves to another element.
- **Restarts:** numbers never restart within a daemon run. The three-letter prefix comes from a counter that goes up at every daemon start, so a ref from before a restart fails cleanly instead of naming something else.

## 5. Smaller speedups

- **Persistent connection:** one connection carries every request; a new SSH channel per call would cost about 7 ms.
- **Name index:** an exact name that one element had at the last read is resolved in under 1 ms, after checking the element still exists, is enabled and on screen. A batch like "invoke Seven, Plus, Eight" never searches.
- **Window cache:** the last match for a window query is re-checked instead of listing all windows.
- **1 ms timer:** `timeBeginPeriod(1)`, so polls and sleeps don't round up to the 15.6 ms system tick.
- **Warm-up:** JIT compilation happens at daemon start, not on the first request.

## 6. Keys, done safely

`SendKeys.SendWait` does not wait for the target app to handle a key. So a check made between two keys can see the old focus while the earlier key is still in flight: `{TAB}secret` could check the user-name field and then type into the password field.

Instead oriel checks once, right before sending: the window is in front, its thread's focus is inside it (`GetGUIThreadInfo`), and the focused element belongs to that process and is not a password field. A single call may also hold text only *before* its first key that can move focus:

- text, escaped characters, repeats, editing and caret keys (arrows, page keys, Home/End with Ctrl or Shift) and Ctrl+A/C/X/V/Z/Y stay in the field;
- Enter and Tab stay put only in a document (a new line, a tab) or a spreadsheet's grid (the next cell); elsewhere they may press a dialog's default button or move to another field, so text after them is refused;
- after anything else, only pure navigation keys may follow: TAB, ENTER, ESC, arrows, PgUp/PgDn, F-keys.

Modifier groups that hold keys (`^(wv)`) and literal control characters are refused before anything is sent. A bracket or parenthesis that no modifier opens types itself, so `=SUM(A1:A3)` needs no escaping.

## 7. The daemon's front door

The daemon listens on 127.0.0.1 only, on a per-user port. Each connection opens with a mutual HMAC handshake over the per-user key in `%LOCALAPPDATA%\oriel\token`:
- the client sends a nonce;
- the daemon proves it holds the key;
- every request carries the client's proof.

The key itself never crosses the socket.

The daemon also closes a connection at the first line that isn't a JSON object. That keeps browser traffic (a page posting to localhost) from ever being read as a request.

**Unknown outcomes.** The client checks that its connection is still alive before sending. If a connection breaks after a request was sent, it says the outcome is unknown instead of sending it again, because repeating an action such as a toggle would undo it.

## 8. A .NET Framework pitfall over Windows OpenSSH

`ssh pc oriel mcp` first stopped after one message. Under Windows OpenSSH, the .NET Framework's console stdin stream (`Console.OpenStandardInput()`) reports end of input as soon as a line arrives after a pause; PowerShell's `[Console]::In` does the same.

A raw `ReadFile` on the same handle works. So oriel reads stdin through a `FileStream` on the raw handle. Any .NET Framework MCP server meant to run over SSH needs the same workaround.

## 9. Building with no SDK

oriel is C# 5 and builds with the `csc.exe` that ships with the .NET Framework. `build/build.ps1` generates the UIA interop from `UIAutomationCore.dll`'s type library (what tlbimp does) and embeds its types into the exe (`/link`). The COM object is created by class id, since interop classes can't be embedded. The result is one ~180 KB file.
