# Demo prompt

Paste the text below into Claude Code once the demo app is open (step 6 in [README.md](README.md)). It names every
check, so the run is short: one read of the window, about five tool calls, then the table.

```text
The window "oriel demo: sign up" is open on this PC. Test its sign-up form with the oriel tools.

Rules, so you never take my mouse or keyboard:
- Read the window as text (tree, find, text) and act only with invoke, set, select and scroll, or a batch of them.
- Never use keys, click, drag, focus, window, launch or close, take no screenshots, and run no commands.
- Use one batch per check where you can.

Checks, reading the Status line after each:
1. Press Sign up with the form empty. Expect a refusal.
2. Fill in Name "Ada Lovelace", Email "ada.example.com", Plan Pro, and accept the terms, then press Sign up.
   Expect the email to be refused.
3. Change the email to "ada@example.com" and press Sign up. Expect a welcome, and Ada at the end of the Members
   list: scroll the list down to see her.
4. Sign up again with Name "Ada Lovelace", Email "ADA@EXAMPLE.COM", Plan Pro and the terms accepted.
   Expect a refusal, because that address is already a member.

End with a short table: check, expected, what happened, pass or fail.
```

What a correct run finds: checks 1 to 3 pass, and check 4 fails, because the app compares email addresses
case-sensitively and lets `ADA@EXAMPLE.COM` sign up a second time (its one deliberate bug; the status line then says 20 members).
