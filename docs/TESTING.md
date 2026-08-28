# Manual testing — Phase 1

These steps assume no prior experience running a .NET project. Follow them in order. If a step
doesn't produce the result described, stop there and report exactly what you saw instead — don't
try to debug it yourself first.

## Before you start

You need:
- The .NET 10 SDK. Check you have it by running `dotnet --version` in a terminal — it should print
  something starting with `10.`. If it prints an error or a different version, see
  `build/install-dotnet-sdk.ps1` for how this machine's SDK was installed to `D:\dotnet` (this
  machine has an older SDK on `C:\Program Files\dotnet` too, so `dotnet` on its own may resolve to
  the wrong one — use the full path `D:\dotnet\dotnet.exe`, or open a new terminal after installing
  so the fixed PATH takes effect).
- Visual Pinball installed somewhere on this machine (any layout — Baller Installer, portable,
  whatever you actually have).

## 1. It builds

```bash
cd "D:\Nudge Frontend"
D:\dotnet\dotnet.exe build Nudge.slnx
```

**Expect:** `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`. If you see any errors, stop —
something is broken and needs fixing before anything else is worth testing.

## 2. The tests pass

```bash
D:\dotnet\dotnet.exe test tests\Nudge.Vpx.Tests\Nudge.Vpx.Tests.csproj
```

**Expect:** a line like `Passed! - Failed: 0, Passed: 80, ...`. The exact count may have grown
since this was written; what matters is `Failed: 0`.

## 3. The app opens and looks right

```bash
D:\dotnet\dotnet.exe run --project src\Nudge.App\Nudge.App.csproj
```

**Expect:** a window titled "Nudge - Set up Visual Pinball" opens within a couple of seconds.
Check:
- The window has the **normal Windows title bar** (minimize/maximize/close buttons, a small icon
  on the left) — this is intentional for Phase 1, not a bug.
- The background is dark, text is light-coloured, buttons have soft rounded corners and a subtle
  shadow. Nothing should look like default grey Windows controls.
- If nothing has been set up before, you'll see **"Where is Visual Pinball installed?"** with a
  single "Browse..." button. If you've used Nudge before and it remembers a folder that's still
  there, it goes straight to **"You're all set"** instead — that's expected, not a bug.

Leave this window open for the next steps.

## 4. Browse to your Visual Pinball folder

Click **Browse...**. A normal Windows folder picker opens.

- Navigate to the folder that directly contains `VPinballX.exe` (or `VPinballX_GL64.exe`, or
  whichever executable your install has) and select it.
- Click **Select Folder**.

**Expect:** within a second or two, the screen changes to **"You're all set"**, showing the name of
your install and its full path.

**If it says the folder isn't a Visual Pinball installation:** check you picked the folder that
directly contains the `.exe` files, not a folder above or below it. If you're sure you picked the
right folder and it's still rejected, that's a real bug — note the exact folder path and what's in
it.

## 5. "Show details" tells the truth about what it found

On the "You're all set" screen, click **Show details**.

**Expect:** a table listing every `.exe` in that folder, with a Build, Architecture, and Version
column for each. Programs that aren't Visual Pinball (an uninstaller, a DMD tool, whatever else
might be in the folder) should show up as `Unknown` build — they should **not** be silently left
out, and they should **not** be guessed at as some VPX flavor they aren't.

Check the architecture column against what you actually know about your files, if you know it
(e.g. a file named with `64` in it that's actually 32-bit should show `x86`, not `x64` — Nudge
reads this from the file itself, not the name).

## 6. Change folder works

Click **Change folder**. **Expect:** you're taken back to the "Where is Visual Pinball installed?"
screen, and can Browse again. Pick the same folder again — it should go straight back to "You're
all set" without complaint.

## 7. Theme toggle actually re-themes everything

Click **Switch to light theme** (or dark, whichever is showing) in the top-right corner.

**Expect:** the *entire* window changes — background, cards, buttons, text — not just the button
itself. This was a real bug earlier in development (only the clicked button changed colour) and is
fixed, but it's worth checking again after any future theming change.

Click it again to switch back. **Expect:** it returns to exactly how it looked before.

## 8. Close and reopen — it remembers

Close the window. Run step 3 again.

**Expect:** it goes straight to "You're all set" with the same folder as before, no prompt. This is
intentional: Nudge remembers a folder once you've explicitly told it, and doesn't make you re-pick
it every launch.

## 9. Read the log file

Open `%LocalAppData%\Nudge\logs\` in File Explorer (paste that path into the address bar). There
should be a file named like `nudge-20260101.log` for today.

Open it in Notepad. **Expect:** readable log lines describing what Nudge did — discovery attempts,
what was found and rejected, settings saved. Check that your Windows username does **not** appear
anywhere in the file — paths should show `<user>` in place of it (e.g.
`C:\Users\<user>\AppData\...`). If your actual username appears anywhere in the log, that's a
privacy bug worth flagging immediately.

## What "done" looks like

If every step above matched its "Expect" line, Phase 1 is working as intended. Note anything that
didn't match, however small, rather than assuming it's fine — that's what this document is for.
