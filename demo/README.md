# Demo kit

Everything for one real-time take, 60 to 90 s long: Claude Code tests a small Windows Forms sign-up form through oriel,
while you type in Notepad on the same screen. oriel acts through UI Automation (invoke, set, select, scroll), so the
agent never takes your keyboard or mouse, and you keep typing the whole time.

Caption: **real time; element actions only**

| File | What it is |
|---|---|
| `SignUpApp.cs`, `build.ps1` | The app under test: name, email, plan, terms, a status line and a members list. It has one deliberate bug: the duplicate email check is case-sensitive. `build.ps1` compiles it with the C# compiler that comes with Windows, to `bin\oriel-demo.exe`. |
| `PROMPT.md` | The prompt to paste into Claude Code. |
| `claude-settings.json` | Settings for the Claude Code session: oriel's read and element tools run without a permission prompt; `keys`, `click`, `drag`, `focus`, `window`, `launch`, `close` and the shells are removed. A `batch` can still hold `keys` or window steps, because permission rules can't look inside it, so `check-trace.py` is the proof. |
| `record.ps1` | Records the whole screen with ffmpeg (gdigrab), mouse pointer included, no sound. It doesn't install ffmpeg. |
| `check-trace.py` | Reads the Claude Code session and fails if any `keys`, `click`, `drag`, window or shell call, or any call to another MCP server, is in it. |

Checked on Windows 11 with oriel 0.1.0 (2026-11-10): every control the prompt needs answers to invoke, set, select
and scroll, with the foreground window and the mouse pointer unchanged. Windows 10 is untested.

## Steps

1. **Create a separate local account**, so no names, notifications or tabs from your own account get into the video.
   This changes the PC's accounts, so it's yours to run, in PowerShell as administrator:

   ```powershell
   net user oriel-demo * /add
   ```

   It asks for the new password twice. The account is a standard user (group Users). Sign in to it with Start, your
   account picture, `oriel-demo`; your own session stays signed in.

2. **In the demo account**, set up the screen for a readable video (these settings apply to this account only):
   - Settings, System, Display, Scale: 200% on a 4K screen, so a 1080p cut stays readable.
   - Settings, System, Notifications: turn on Do not disturb.
   - Optional: Settings, Personalisation, Taskbar, Taskbar behaviours, "Show seconds in system tray clock". The
     running clock shows the take has no cuts.

3. **Install** Claude Code, oriel and this kit, in PowerShell in the demo account (no administrator needed):

   ```powershell
   irm https://claude.ai/install.ps1 | iex
   ```

   Open a new PowerShell window, run `claude` once to sign in, then `/exit`. Install oriel as in the main
   [README](../README.md#install) (download `oriel.exe`, then `.\oriel.exe install`), and add it to Claude Code:

   ```powershell
   claude mcp add --scope user oriel -- "$env:LOCALAPPDATA\Programs\oriel\oriel.exe" mcp
   Invoke-WebRequest -UseBasicParsing https://github.com/YordiLorenzo/oriel/archive/refs/heads/main.zip -OutFile $env:TEMP\oriel-main.zip
   Expand-Archive $env:TEMP\oriel-main.zip $HOME
   powershell -ExecutionPolicy Bypass -File $HOME\oriel-main\demo\build.ps1
   ```

   Get ffmpeg too: run `record.ps1` without it and it says where.

4. **Lay out the screen:** start `$HOME\oriel-main\demo\bin\oriel-demo.exe`, open Notepad, and put both beside the
   terminal. Start Claude Code in an empty folder, so it can't read the app's source:

   ```powershell
   New-Item -ItemType Directory -Force $HOME\demo-run | Out-Null; cd $HOME\demo-run
   claude --settings $HOME\oriel-main\demo\claude-settings.json
   ```

   Accept the folder trust question. `/mcp` should list oriel as connected.

5. **Rehearse once**, without recording. Paste the prompt from [PROMPT.md](PROMPT.md). It should end with the table
   in under 90 s, and check 4 should fail. Check its trace (step 7). Record a 5 s test clip with the
   command from step 6 plus `-Seconds 5` (run it through `powershell -ExecutionPolicy Bypass -File`, as there: Windows
   blocks a plain `.\record.ps1` by default) and play it: if a window shows black, record with OBS Studio's Display Capture instead.
   Then close and reopen the app, `/exit`, and start `claude` again as in step 4, so the take is a new session.

6. **Record the take.** In a second PowerShell window:

   ```powershell
   powershell -ExecutionPolicy Bypass -File $HOME\oriel-main\demo\record.ps1 -Ffmpeg <path to ffmpeg.exe> -Out C:\Users\Public\Videos\oriel-demo.mkv
   ```

   It records 120 s of the whole screen. Minimise it, click into Claude Code, paste the prompt and press Enter. Then
   click into Notepad and keep typing until the table appears. Don't touch the app.

7. **Check the trace.** With Python 3 on the PC ([python.org](https://www.python.org/downloads/windows/)):

   ```powershell
   python $HOME\oriel-main\demo\check-trace.py
   ```

   It reads the newest Claude Code session (or the `.jsonl` file you name, from `$HOME\.claude\projects\`) and its
   subagents, prints every tool used, and ends with PASS or FAIL. FAIL means record again. Without Python here, copy
   the session file to a computer that has it.

8. **Publish** with the caption "real time; element actions only" at the start of the video and in the post, and
   say the app has one deliberate bug.

9. **Remove the demo account** when the take is done. Copy what you need out of it first (the video is in
   `C:\Users\Public\Videos`; the session file is under `C:\Users\oriel-demo\.claude\projects`). Sign it out, then in
   PowerShell as administrator:

   ```powershell
   net user oriel-demo /delete
   Get-CimInstance Win32_UserProfile | Where-Object LocalPath -eq 'C:\Users\oriel-demo' | Remove-CimInstance
   ```

   The second line deletes the account's profile folder and everything in it.
