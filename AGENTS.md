# AGENTS.md — Instructions for AI coding agents working on Nudge
Read this file completely before writing any code. It encodes decisions that have
already been made and research findings that are expensive to rediscover. Do not
re-derive them and do not contradict them without saying so explicitly.
---
## 1. What Nudge is
Nudge is an open-source Windows desktop frontend, game library and launcher for
**Visual Pinball X (VPX)**. It finds the user's VPX installation, scans their table
collection, presents it as browsable artwork, and launches tables in Desktop or VR.
Think Steam or Playnite, built specifically for VPX.
**Core loop:** open app → see tables as artwork → click one → click Play → VPX launches
→ VPX exits → back to the library.
**The actual differentiator** is not the grid, it's the **health system**: telling users
which tables are broken and why. The grid is what makes people install it; health is what
makes them keep it.
---
## 2. Who the maintainer is
The project owner is a **complete beginner at software development**. This changes how
you work:
- Explain technical decisions in plain language, not jargon.
- When they need to do something, give exact steps: what to install, what command to run,
  what result to expect, what to do if it fails.
- Do not dump thousands of lines in one response.
- Do not assume they can debug your output. If it breaks, you debug it.
- Prefer boring, well-documented approaches over clever ones.
**Critically: they are the build machine.** Verify what you can, and state plainly what
you have not verified. Never claim something works because it compiled in your head.
---
## 3. Locked decisions — do not revisit without asking
| Decision | Value |
|---|---|
| Language / runtime | C# on .NET 10 |
| UI framework | **WPF** (chosen over Avalonia for tutorial availability and simplicity) |
| Database | SQLite via EF Core 10, with migrations |
| Logging | Serilog, structured, rolling files |
| VPX file reading | OpenMcdf (OLE compound documents) |
| Archives | SharpCompress |
| Filesystem abstraction | System.IO.Abstractions |
| Testing | xUnit + FluentAssertions + NSubstitute |
| Packaging | Velopack installer + portable ZIP |
| Product name | **Nudge** |
| License | MIT |
| Minimum OS | Windows 10 21H2 |
| Deliverable | `Nudge.exe` — a normal Windows executable, not MSIX |
WPF's default controls look dated. That is irrelevant — every control is custom-templated
through the theme system (Section 7). Never ship default WPF chrome.
---
## 4. VPX research findings — authoritative, do not guess around these
Sourced from the official `vpinball/vpinball` repository and its shipped docs. If you
believe one of these is wrong, say so explicitly and cite a source; do not silently
override it.
### 4.1 Build flavors
VPX ships **three rendering flavors**, each as a separate executable, in x86 and x64:
| Flavor | Typical exe | VR support | Status |
|---|---|---|---|
| **BGFX** | `VPinballX_BGFX.exe` | **OpenXR** | Current, recommended, introduced 10.8.1 |
| **OpenGL** | `VPinballX_GL.exe` / `_GL64.exe` | **OpenVR** (needs SteamVR) | Deprecated, VR being removed |
| **DirectX 9** | `VPinballX.exe` | **None** | Largely deprecated, reference build only |
Legacy `VPinball995.exe` (VP9, `.vpt` tables) often sits in the same folder. Classify it
as `VP9Legacy`, no VR.
**Never infer flavor from filename alone.** Combine: filename hint + Win32 version resource
+ PE header architecture + presence of flavor-specific sibling DLLs. Attach a confidence.
If you cannot classify, return `Unknown` — never a guess.
### 4.2 Command line — the complete supported set
```
-Play [file]        Load and play a table          <- the launch verb
-Edit [file]        Load into editor
-Ini [file]         Use a custom settings file     <- how VR is selected
-Minimized          Invisible minimized window mode
-ExtMinimized       Same, with Pause Menu enabled
-Primary            Force render on primary monitor
-EnableTrueFullscreen / -DisableTrueFullscreen
-Pov [file]         Load, export POV, close
-PovEdit [file]     Camera mode, export POV on exit
-ExtractVBS [file]  Export table script and close
-Audit [file]       Audit the table
-ListRes            Enumerate display resolutions
-ListSnd            Enumerate sound devices
-c1 .. -c9 [value]  Custom params, readable via GetCustomParam(n)
-RegServer / -UnregServer
-LessCPUthreads
```
**There is no `-VR` flag. No `-Desktop` flag. No `-Fullscreen` flag.** Do not invent them.
### 4.3 VR is settings-driven, not argument-driven
VR mode is selected by the settings file, not the command line. On 10.8.0 the OpenGL build
autodetects a SteamVR driver install; absent that it starts in 2D.
**The mechanism for "PLAY IN VR":**
```
VPinballX_GL64.exe -Ini "<Nudge's own VR profile.ini>" -Play "<table.vpx>"
```
Nudge owns that ini file, inside Nudge's own data directory. See Section 6 for the rules.
### 4.4 Settings files
Since 10.8, VPX settings live in `%AppData%\VPinballX\VPinballX.ini`. Can be relocated
beside the exe (portable mode). A custom file can be supplied with `-Ini`.
**A file named exactly like the table but with `.ini` extension, placed next to the table,
acts as a per-table override.** The name must match exactly. This is the correct
non-destructive way to store per-table tweaks.
Read `TablesDirectory`, `MusicDirectory`, `ScriptsDirectory` from the ini rather than
assuming `<install>\Tables`.
### 4.5 Table files
`.vpx` is a **Microsoft OLE Compound Document** (magic `D0 CF 11 E0 A1 B1 1A E1`), NOT a
zip. Storages: `GameStg` and `TableInfo`. Typical size 2–150 MB.
- `TableInfo` holds author metadata — **frequently blank or inherited-and-wrong**, because
  most tables are mods of mods. Filename parsing is often *more* accurate.
- **The PinMAME ROM name is not metadata.** It is a `cGameName` assignment inside the
  table's VBScript in `GameStg`. Extracting it requires parsing the script.
- During a fast scan, read only the small `TableInfo` streams. Never load a whole file.
  Script extraction is a second-pass background operation.
### 4.6 Surrounding ecosystem
- **VPinMAME** is a registered COM object. All VPX installs on a machine **share one
  registration**. ROM folder comes from `HKCU\Software\Freeware\Visual PinMame\globals\rompath`
  (fallback `HKLM`). ROMs are `.zip` files, kept zipped, named after the ROM name.
- **B2S**: `<TableName>.directb2s` next to the table. B2S Server is a COM object in the
  `Tables` folder. In 10.8.1 this is becoming an in-process plugin (`B2S Legacy`), enabled
  via the live UI plugin manager. **Health rules must be version-aware** or they will
  produce confident wrong answers.
- **Scores**: VPX does not report scores. Community reads PinMAME NVRAM via PINemHi.
  Do not claim score tracking.
- **Baller Installer** (the maintainer's setup) ships VPX 10.8.0 Final build 2058, x64
  GL/DX plus x86, VPinMAME 3.6.0, B2S 2.1.3, FlexDMD 1.9.1, and legacy `VPinball995.exe`.
  **No BGFX build.** Baller users are on the OpenVR/SteamVR path by default.
---
## 5. Architecture
```
Nudge.App        WPF UI, Views, ViewModels, Themes
      |  (depends only on interfaces in Core)
Nudge.Core       Domain models, service interfaces, business rules
                 NO file I/O. NO UI code. NO database code.
      |
Nudge.Vpx        Installation discovery, flavor detection, ini read/write,
                 table file parsing, launch engine
Nudge.Library    Scanning, grouping, metadata, health, import
Nudge.Media      Artwork providers, thumbnail cache
Nudge.Data       EF Core, SQLite, migrations, repositories
```
**The one rule:** `Nudge.Core` defines interfaces; everything else implements them; the UI
depends only on `Core`.
Concretely — `Nudge.App` **never** calls `File.Exists`, **never** calls `Process.Start`,
**never** opens a database connection. It asks an injected service.
Note for the maintainer's benefit: these dot-names are **project names**, not files. They
compile to `Nudge.Vpx.dll` etc. alongside `Nudge.exe`. Nothing here creates `.vpx` files.
### Do not add
No message bus. No CQRS. No abstraction over an abstraction. No plugin host until the
launcher works. The instruction was explicit: do not over-engineer.
---
## 6. Safety rules — non-negotiable
**File writes**
- Never silently delete. Never silently overwrite. Confirmation dialogs name specific files.
- Imports default to **copy**, never move.
- There is no "clean up my library" feature and there will not be one.
**Ini files** — the maintainer approved ini writing with a condition: *it must not interfere
with anything a user would otherwise need.* That means:
- Nudge **may** create and manage ini files **inside its own data directory** and pass them
  with `-Ini`.
- Nudge **never** modifies `%AppData%\VPinballX\VPinballX.ini` or anything inside the user's
  VPX installation — except a specific per-table override the user explicitly requested, with
  a confirmation dialog and an automatic backup.
- Everything written is logged and reversible.
**Archive extraction** — the single most likely place for a real vulnerability:
- Resolve every entry path against the intended destination; reject anything that escapes
  (`../`, absolute paths, drive-relative paths).
- Refuse symlink and junction entries outright.
- Enforce uncompressed-size and entry-count limits (zip bombs).
- There must be a dedicated test suite of malicious archives.
**Execution**
- Nudge launches VPX executables the user configured, and nothing else.
- Dropping an `.exe` on the window does nothing. Archive extraction skips executable entries
  with a visible notice.
**Privileges** — never require administrator. If a feature would need it, skip the feature
with an explanation rather than prompting for elevation.
**Logs** — full paths are logged (diagnostics need them), but redact the Windows username.
Provide a "copy sanitised log" button so users can paste into forums safely.
**Path handling** — handle >260 char paths explicitly. Reject reserved device names
(`CON`, `PRN`, `AUX`, `NUL`, `COM1`…). Support UNC but flag as slow.
---
## 7. Coding standards
- Clear names over short names. Small functions.
- Comments explain **why**, not what.
- Constructor injection for dependencies. No service locator, no statics holding state.
- All I/O is `async`. **The UI thread never blocks.** This is a hard requirement.
- Every filesystem call goes through `IFileSystem` so it can be faked in tests.
- Return `Result<T>` for operations that can fail expectedly; exceptions for genuine bugs.
- **Confidence is a first-class concept.** Detection results carry a confidence and the
  evidence behind them. Never present a guess as a fact.
**Theming rule:** every visual value comes from a named resource. If a view contains
`Background="#1a1a2e"`, that is a bug. No hard-coded colours, fonts, or spacing in views.
---
## 8. Performance budgets
Assume 1,000 tables and 5,000 media files.
| Metric | Budget |
|---|---|
| Cold start to visible grid (warm cache) | < 2 s |
| Search results | < 50 ms |
| Scrolling | 60 fps |
| Full rescan, 1,000 tables | < 60 s |
Required techniques: UI virtualization (non-negotiable), decode images at target size never
full size, disk thumbnail cache, incremental scanning (compare path + size + mtime), batched
DB writes (one transaction per few hundred rows, not per row), grid queries as projections
not full entity loads.
If a milestone misses a budget, fix it in that milestone.
---
## 9. Phase discipline — the most important process rule
Build in order. Do not start the next phase until the current one is verified working.
| Phase | Content | Status |
|---|---|---|
| 1 | Skeleton, CI, logging, DI, settings. VPX installation discovery + executable identification. Setup screen. | **Done** |
| 2 | Table file reading: OLE parsing, metadata, filename parsing, confidence. Headless. | **Done** |
| 3 | Database: schema, EF Core, migrations, repositories. Scanner writes to it. Headless. | **Done** |
| 4 | The grid: virtualized library UI, thumbnails, search. | |
| 5 | Table detail + launch engine. Desktop launch end to end. **MVP.** | |
| 6 | VR: profile management, capability detection, PLAY IN VR. | |
| 7 | Health checking, favourites, recently played, sorting, filters. | |
| 8 | Collections, metadata editing, artwork assignment, duplicate detection. | |
| 9 | Import: drag-and-drop, archives, import preview, safety. | |
| 10 | Frontend media import (PinballX/Y/Popper). Theme polish. | |
| 11 | Cabinet mode, controller support, multiple installations UI. | |
| 12 | Backup/export, richer artwork, auto-update. | |
**Phases 1–3 status corrected from the original plan**: all three are complete and
committed as of this note. See `docs/IMPLEMENTATION-STATUS.md` for exactly what each
phase built and how it was verified, and the session handoff documents for anything not
yet folded into that file.
**Phase 1 explicitly excluded (now built in Phases 2–3, still excluded from Phase 1
itself):** database, table scanning, the grid, artwork, search, launching, VR beyond
*reporting* capability, import, health, collections, controllers.
Every phase ends with: it builds, tests pass, `docs/IMPLEMENTATION-STATUS.md` updated, the
maintainer has manually tested it against `docs/TESTING.md`, known limitations written down.
**Resist scope creep.** "Just add the scanner too" is how this project stops being testable.
---
## 10. Working agreement
**Decide yourself** when a choice is low-risk and reversible. Do not ask permission for
naming, file layout, or minor library choices.
**Ask** when a decision has major architectural consequences or is hard to reverse.
**Inspect before assuming.** Read the actual project files rather than guessing what is there.
**Run tests when you can.** Debug failures rather than reporting them.
**Never fabricate results.** Do not say something works unless it was verified. Where you
cannot verify — anything needing a real VPX install or a VR headset — say so explicitly and
give the maintainer steps to check it.
**When sources conflict**, say so, explain the conflict, determine which applies to current
VPX, and record it in `docs/RESEARCH-NOTES.md`. Do not silently pick one.
---
## 11. Repository layout
```
nudge/
├── .github/workflows/         build.yml, release.yml
├── docs/
│   ├── ARCHITECTURE.md        maintained, not written once
│   ├── RESEARCH-NOTES.md      VPX findings, updated as we learn
│   ├── IMPLEMENTATION-STATUS.md   what is done, what is not
│   ├── TESTING.md             manual test steps per milestone
│   └── adr/                   one file per significant decision
├── src/
│   ├── Nudge.Core/
│   ├── Nudge.Vpx/
│   ├── Nudge.Library/
│   ├── Nudge.Media/
│   ├── Nudge.Data/
│   └── Nudge.App/
├── tests/
│   ├── Nudge.Vpx.Tests/
│   ├── Nudge.Library.Tests/
│   ├── Nudge.Data.Tests/
│   └── Nudge.TestSupport/     fake filesystems, sample fixtures
├── build/
├── README.md  LICENSE  CONTRIBUTING.md  CHANGELOG.md  SECURITY.md
└── Nudge.sln
```
**Actual repo layout as of Phase 3** differs slightly from the plan above: the solution
file is `Nudge.slnx` (the .NET 10 SDK's new XML format, not `.sln` — every `dotnet` command
works with it identically), there is no `Nudge.Media` project yet (not needed until artwork
work begins), `docs/ARCHITECTURE.md` and `docs/adr/` were not created (not yet needed at
this project size), and `CONTRIBUTING.md`/`CHANGELOG.md`/`SECURITY.md` do not exist yet.
`.config/dotnet-tools.json` also exists now (the `dotnet-ef` local tool, needed for Phase 3
migrations).
---
## 12. Test environment
The maintainer's machine, which is what Phase 1 must work against:
- Windows 10+
- VPX installed via **Baller Installer** (latest) → VPX 10.8.0 Final, build 2058
- Executables present: `VPinballX.exe` (DX9), `VPinballX_GL64.exe` (OpenGL/x64),
  `VPinball995.exe` (VP9 legacy), plus x86 variants
- **No BGFX build**
- 40+ tables
- **Meta Quest 2** headset → VR path is OpenVR via SteamVR (Link/Air Link or Virtual Desktop)
Phase 1 succeeds if all three executable types are classified correctly on this machine,
with correct versions, and VR capability reported honestly.

**Real installations found during Phases 1–3 development** (see `docs/RESEARCH-NOTES.md`
and the session handoff documents for detail): the maintainer's actual Baller install turned
out to be considerably messier than this section's clean four-executable description —
real unofficial builds, legacy VP9 variants, and a VPinMAME folder full of unrelated tools
were all present and had to be handled correctly, which they were. A second, separate test
installation was also used throughout Phases 2–3 specifically for iterative testing.
