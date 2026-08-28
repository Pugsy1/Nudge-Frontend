# Implementation status

Updated as work lands. This describes what actually exists, not what's planned — see the phase
table in `AGENTS.md` for the roadmap.

## Phase 1 — Skeleton, discovery, identification, setup screen

**Status: functionally complete, not yet manually signed off by the maintainer.** See
`docs/TESTING.md` for the steps to do that.

### What's built

**Solution structure** (`Nudge.slnx` — the .NET 10 SDK now generates the new XML solution format
instead of `.sln`; every `dotnet` command works with it exactly the same way):

- `src/Nudge.Core` — models, interfaces, `Result<T>`, evidence, no I/O
- `src/Nudge.Vpx` — installation discovery, executable identification, settings, all behind
  `System.IO.Abstractions` so it's fully testable without touching a real disk
- `src/Nudge.App` — WPF UI, MVVM via CommunityToolkit.Mvvm, Serilog logging with username
  redaction, Dark/Light theming
- `tests/Nudge.Vpx.Tests` — 80 tests, all passing
- `tests/Nudge.TestSupport` — synthetic installation layouts, a hand-built PE image generator so
  architecture detection is tested against real parsed PE headers rather than a stub, fakes for the
  registry/version-resource/environment

**VPX installation discovery** — layered strategy exactly as specified in `AGENTS.md` section 4.3:
registry (COM registration + VPinMAME's `rompath`) → known conventional paths, probed across every
fixed drive → `VPinballX.ini` directory hints → manual folder picker as final authority. Every
layer produces candidates only; a separate validator decides what's real by checking the disk.

**Executable identification** — architecture read from the PE header via `PEReader`, never from the
filename. Flavor determined by combining filename, Win32 version resource, and sibling support
libraries (`openvr_api64.dll`, `bgfx*.dll`), with a `Confidence` and a plain-English evidence trail
attached to every result. Verified against the maintainer's real, messy installation (see
`docs/RESEARCH-NOTES.md`) — not just synthetic test data.

**Setup screen** — a single, simple flow: Browse to a folder, or accept nothing automatically.
Picking a valid folder confirms it immediately and lands on a "You're all set" screen; there is no
separate confirm step. Returning launches skip straight to that screen if the folder you
previously chose still checks out. Nudge never scans the machine and offers a suggestion, and
Browse never pre-seeds a starting directory — every folder Nudge uses came from the user pointing
at it, this session or a previous one.

**Theming** — `Themes/Colors.Dark.xaml` and `Colors.Light.xaml` define the same set of keys; every
colour, font size, and spacing value used anywhere in the UI is a named `DynamicResource`, with
zero literals in any view. The palette is swapped live at `Application.Resources.MergedDictionaries`
top level — swapping a nested wrapper dictionary was tried first and doesn't reliably propagate
`DynamicResource` invalidation to already-rendered elements in WPF; swapping the top-level entry
directly does. A light neumorphic pass sits on top: soft `DropShadowEffect` shadows, low-contrast
surfaces, rounded corners, a "pressed in" state on click.

**App icon** — a hand-rendered, size-aware chrome disc icon (`Assets/nudge.ico`), rendered natively
per resolution rather than downscaled from one master, so it stays legible at 16px. The header
wordmark is currently plain text, pending a supplied logo PNG.

**Standard Windows title bar** — by explicit decision, custom window chrome is deferred to Phase 4
alongside the real library shell.

### What's deliberately not built (Phase 1 scope)

Per `AGENTS.md` section 9: no database, no table scanning, no `.vpx` parsing, no grid, no artwork,
no search, no launching, no VR beyond capability reporting, no import, no health checking, no
collections, no controllers. `IVpxInstallationDiscovery` can classify VR capability per executable
(`OpenVR` / `OpenXR` / `None` / `Unknown`) because that's cheap to derive alongside flavor
detection, but the UI does not currently surface it — VR is explicitly a later, more deliberate
feature.

### Known limitations

- **VR capability detection is unverified against real VR hardware.** The classification logic
  (OpenGL → OpenVR, BGFX ≥ 10.8.1 → OpenXR) follows documented behaviour but has not been checked
  against a running SteamVR or OpenXR session, because Phase 1 doesn't launch anything. Confidence
  in the detection *rule* is high; confidence that it matches lived behaviour on the maintainer's
  Quest 2 setup is unverified.
- **Confidence indicators are computed but not shown in the UI.** The `Confidence`/evidence system
  is fully implemented and tested, but every visible confidence badge was removed from the setup
  screen at the maintainer's request, to be reintroduced later as a toggleable setting. The
  underlying data is still there; only the display was pulled.
- **The header has no logo yet.** Plain "Nudge" text, pending a supplied PNG asset.
- **The "known paths" discovery layer's drive probing was validated against `MockFileSystem`'s
  `DriveType.Unknown` behaviour**, which exposed a real bug (an allow-list of `DriveType.Fixed` that
  would have silently skipped any drive Windows reports a different type for) and was fixed to a
  deny-list instead. Real-world drive-type coverage beyond the maintainer's machine (which has C:,
  D:, E: as plain fixed NTFS volumes) is unverified.
- **Nothing has been tested with SteamVR or a Meta Quest 2 attached**, since Phase 1 does nothing
  that would exercise that path.

### Verified vs. not verified

| Claim | Verification |
|---|---|
| `dotnet build` succeeds, 0 warnings, 0 errors | Verified, repeatedly, throughout development |
| `dotnet test` passes | Verified: 111/111 passing as of this writing (80 from Phase 1, 31 added in Phase 2) |
| The app launches and shows a themed window | Verified via screenshot of the running process |
| Theme toggle re-themes the whole app live | Verified via screenshot + direct resource-lookup logging after the top-level-swap fix |
| Discovery finds a real installation on the maintainer's machine | Verified — see `docs/RESEARCH-NOTES.md` |
| The maintainer has run through `docs/TESTING.md` themselves | **Not yet** |
| VR capability matches real SteamVR/OpenXR behaviour | **Not verified** — nothing in Phase 1 launches VR |

## Phase 2 — Table file reading

**Status: functionally complete for its stated scope (OLE parsing, metadata, filename parsing,
confidence), headless. Not yet wired into anything the UI shows.**

### What's built

**`ITableFileReader`** (`Nudge.Vpx.TableFiles.VpxTableFileReader`) reads a single `.vpx` file and
produces a `VpxTableFile`: the raw OLE `TableInfo` metadata, the raw filename hints, a reconciled
display title, and a `Confidence`/`DetectionEvidence` explaining the reconciliation — the same
pattern Phase 1 established for executable identification, applied here to table metadata.

- **`OleTableInfoReader`** opens a `.vpx` file's `TableInfo` OLE storage via OpenMcdf (the locked
  dependency for this) and decodes each stream as UTF-16LE text. Never reads `GameStg`, which is
  where the bulk of a table's size actually is.
- **`TableFilenameParser`** parses the loose `"Title (Manufacturer Year).vpx"` convention, with
  trailing mod/version tags kept as free text. Produces an honest empty or partial result for
  filenames that don't fit the convention, rather than a wrong guess.
- **Reconciliation**: when OLE metadata and the filename disagree, the filename wins for the
  display title (table metadata is frequently stale — see docs/RESEARCH-NOTES.md), but both raw
  values are kept on the record and the disagreement is recorded as evidence, never silently
  resolved.

**Verified two ways**, matching how Phase 1's executable identification was verified:

1. **Synthetic files** built with the real OpenMcdf writer (`Nudge.TestSupport.SyntheticVpxFile`),
   covering: every `TableInfo` field present, fields absent, an entirely empty table, a valid OLE
   file with no `TableInfo` storage at all, and a file that isn't an OLE document at all. 27 tests.
2. **33 real table files** from the maintainer's actual collection, spanning 12 different table
   authors and up to 460 MB each — read via a throwaway harness built against the real production
   classes (not a reimplementation), with zero failures and a confidence distribution that matches
   what real, messy community data should produce (15 High, 16 Medium, 2 Low — the two Low results
   were test/calibration tables with no real title anywhere, which is the honest answer for them).

### What's deliberately not built (Phase 2 scope)

- **No folder scanning.** This reads one file at a time; walking a directory of many tables,
  grouping duplicates, and incremental re-scanning is `Nudge.Library`'s job, arriving with Phase 3
  ("Scanner writes to it" per the phase table in `AGENTS.md`).
- **No PinMAME ROM name extraction.** The ROM name is a `cGameName` assignment inside the table's
  VBScript, embedded in `GameStg`, not a simple metadata field. AGENTS.md section 4.5 explicitly
  frames this as a second-pass background operation, separate from the fast metadata scan — it is
  deferred, not forgotten.
- **No database.** `VpxTableFile` records are produced and immediately discardable; nothing
  persists yet. That's Phase 3.
- **Nothing in the UI uses this yet.** Phase 2 is headless by design, matching the phase table.

### Known limitations

- **The `TableInfo` UTF-16LE encoding finding, and the "roughly half of filenames follow the
  convention" finding, are drawn from 33 real files on one collection.** They match AGENTS.md's
  prior documented expectations exactly, which is reassuring, but a wider sample could still turn
  up an edge case (a different VPX export tool, a much older table) not seen here.
- **Filename manufacturer/year parsing accepts years 1930–2049** as a plausibility check. This is a
  deliberately loose, generous range for a best-effort hint, not a claim about pinball history.
- **The title-reconciliation "do they roughly agree" check is a simple normalised-substring
  comparison**, not a fuzzy string match. Two genuinely different titles that happen to share a
  common substring could register as "agreeing" when they shouldn't. Not seen in the 33-file real
  sample, but not proven absent either.
