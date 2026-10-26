# Tools

Every tool replies with text and ends with `(<action> took N ms)`. Errors come back as tool errors whose text says what to do next. For example, a missed name lists the closest elements.

The tools carry `_meta: {"anthropic/alwaysLoad": true}`, so Claude Code loads them up front instead of behind a tool search, which saves a model turn at the start of a session. The server's instructions are fixed text: window titles and page content, which apps and web pages control, only ever come back as tool results.

**Windows** are named by part of their title, their process name (`chrome`, `notepad`), or their handle, as `windows` lists them. **Elements** are named by ref (`kqm17`, from `tree` or `find`) or by window plus name. The best name match wins: exact before partial, enabled and on-screen first.

## Reading

| Tool | Arguments | Reply |
|---|---|---|
| `status` | none | locked or not, seconds since the user last touched the PC, the foreground window |
| `windows` | none | `[handle] process "title"` per visible window, front first; `(off limits)` marks windows oriel won't touch |
| `tree` | `window`, `depth` (12), `max_elements` (400) | the window's header, then one line per named or actionable on-screen element, indented by nesting |
| `find` | `window`, `name`, `type` | elements whose name contains `name` (off-screen ones too, marked) |
| `text` | `id` or `window` + `name` (+ `type`) | the text of a document or field |
| `wait` | `window`, `name`, `type`, `timeout` (10 s) | with `name`: the element, once it exists (re-checks on the window's own events); with only `window`: the window, once it appears; with neither: a pause (3 s), then the open windows |
| `screenshot` | `window` (the one in front), `max_width` (1280) | a PNG of the window (an MCP image), even when it is covered |

A tree line looks like this:

```
[kqm23] ComboBox "Size" id=size value="Small" collapsed {expand,set}
```

It holds the ref, the control type, the name, the automation id (when meaningful), the value, the state, and `{actions}`. Actions are listed only when they differ from the type's usual one:
- buttons, links and menu items invoke;
- check boxes toggle;
- list, tab and tree items and radio buttons select;
- combo boxes expand;
- edits and sliders set.

Other markers: `{none}` means the control can't be acted on; `(off screen)` means it is scrolled away. A scrollable area also shows where it stands: `(scrolled 0%, more below)`.

## Acting

Each of these replies with what changed in the window once it settles (`changes: false` skips that). `invoke`, `set`, `select` and `scroll` act through UI Automation and don't take keyboard focus or move the mouse. `keys`, `click` and `drag` send real input: they bring the window to the front, check it, and wait for a pause in the user's own typing and mouse use (see [SECURITY.md](../SECURITY.md)).

| Tool | Arguments | Does |
|---|---|---|
| `invoke` | `id` or `window` + `name` (+ `type`) | press, toggle, select or expand |
| `set` | `value`, `id` or `window` + `name` | replace a field's text (`""` clears it), or set a slider to a number |
| `select` | `option`, `id` or `window` + `name` | open a combo box, list, tab strip or menu, pick the item, close it |
| `scroll` | `id` or `window` (+ `name`), `direction` (down), `amount` (page/line) | scroll an item into view, or a container |
| `keys` | `keys`, `window` (+ `name`/`id`), `literal` | SendKeys syntax (or plain text with `literal`), after the safety checks in [SECURITY.md](../SECURITY.md); arrows, page keys and Ctrl/Shift+Home/End count as editing keys, and text may follow Enter or Tab only in a document or a spreadsheet's cells |
| `click` | `id` or `window` + `name` (its center), or `window` + `x`, `y` (window pixels), `button` (left), `double` | click with the real mouse, for what has no UIA action (cells, canvases); the window comes to the front first, the point must be inside it and not covered |
| `drag` | `window`, `x1`, `y1`, `x2`, `y2` | press at the first point, release at the second (in 20 steps): drawing, sliders, selecting a range |
| `batch` | `steps`, `window` | several actions in one call; the report covers the whole batch |
| `screenshot=true` on invoke, set, keys, click, drag, batch | | the action's reply plus a picture of the window afterwards (1024 px wide), so checking by eye costs no extra call |

A change report looks like this:

```
invoked "Place order"
~ [crh27] RadioButton "Express delivery" selected
+ [crh42] Text "Order placed: Ada Lovelace, Large, express, gift wrap, quantity 3"
- Text "No order yet"
```

Other replies:
- `no visible change in the window`;
- `window closed`;
- `+ window [handle] process "title"` followed by that window's tree;
- for big changes, `the window changed a lot (N changes); now:` and the new tree.

## Windows and programs

| Tool | Arguments | Does |
|---|---|---|
| `launch` | `path`, `args`, `wait_title`, `timeout`, `tree` (true), `max_elements` (150) | start a program, file, folder or URI; with `wait_title`, wait for its *new* window and reply with its tree |
| `focus` | `window` | restore and bring to the front (takes the foreground; needs an unlocked desktop) |
| `window` | `window`, `state` (minimize/maximize/restore), `x`, `y`, `width`, `height` | change a window's state, position or size |
| `close` | `window` | close it (the app may still ask to save) |

## Batch steps

A step is an object with `action` plus that action's options, capitalised: `Window`, `Id`, `Name`, `Type`, `Value`, `Option`, `Keys`, `Path`, `Args`, `WaitTitle`, `Timeout`, `Direction`, `Amount`, `State`. Steps inherit the batch's `window`. Example:

```json
{"window": "Calculator", "steps": [
  {"action": "invoke", "Name": "Seven"}, {"action": "invoke", "Name": "Plus"},
  {"action": "invoke", "Name": "Eight"}, {"action": "invoke", "Name": "Equals"}]}
```

After a step that opens a window or loads a page, add a `wait` step for something on it.

## Command line

The same actions work from a terminal: `oriel call <action> --Window "Character Map" --Name Select`. `--Steps` takes JSON.
