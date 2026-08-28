# Visual Pinball X research notes

This file holds what we know about how Visual Pinball X is built, installed, and configured on a
real machine. It is the reference Nudge's discovery and identification code is built against.

Sourced from the official [`vpinball/vpinball`](https://github.com/vpinball/vpinball) repository
and its shipped documentation, plus direct observation of a real Baller Installer setup during
Phase 1 development. If something here turns out to be wrong or version-specific, update this file
rather than leaving the code and the notes disagreeing — and say so explicitly in the commit
message so the discrepancy isn't silently lost.

## Build flavors

VPX ships **three rendering flavors**, each as a separate executable, in x86 and x64:

| Flavor | Typical exe | VR support | Status |
|---|---|---|---|
| **BGFX** | `VPinballX_BGFX.exe` | **OpenXR** | Current, recommended, introduced 10.8.1 |
| **OpenGL** | `VPinballX_GL.exe` / `_GL64.exe` | **OpenVR** (needs SteamVR) | Deprecated, VR being removed |
| **DirectX 9** | `VPinballX.exe` | **None** | Largely deprecated, reference build only |

Legacy `VPinball995.exe` (VP9, `.vpt` tables) often sits in the same folder. Classified as
`VP9Legacy`, no VR. Real-world installs also carry other VP9-era executables under names like
`VPinball921.exe` or `VPinball99_PhysMod5_Updated.exe` — Nudge matches these by the
`VPinball<digits>` filename pattern plus a version resource whose major version is 9.

**Never infer flavor from filename alone.** Nudge combines: filename hint + Win32 version resource
+ PE header architecture + presence of flavor-specific sibling DLLs, and attaches a confidence to
the result. If it cannot classify an executable, it returns `Unknown` — never a guess. See
`Nudge.Vpx.Identification.VpxExecutableIdentifier`.

## Command line — the complete supported set

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

**There is no `-VR` flag. No `-Desktop` flag. No `-Fullscreen` flag.** Phase 1 does not launch
anything, so none of this is wired up yet — it's recorded here for when the launch engine
(Phase 5) is built.

## VR is settings-driven, not argument-driven

VR mode is selected by the settings file, not the command line. On 10.8.0 the OpenGL build
autodetects a SteamVR driver install; absent that it starts in 2D.

The mechanism for "PLAY IN VR" will be:

```
VPinballX_GL64.exe -Ini "<Nudge's own VR profile.ini>" -Play "<table.vpx>"
```

Nudge will own that ini file, inside Nudge's own data directory, when this is built in Phase 6.

## Settings files

Since 10.8, VPX settings live in `%AppData%\VPinballX\VPinballX.ini`. Can be relocated beside the
exe (portable mode). A custom file can be supplied with `-Ini`.

**A file named exactly like the table but with `.ini` extension, placed next to the table, acts as
a per-table override.** The name must match exactly. This is the correct non-destructive way to
store per-table tweaks, when Nudge starts writing them (never in Phase 1).

Nudge reads `TablesDirectory`, `MusicDirectory`, `ScriptsDirectory` from the ini rather than
assuming `<install>\Tables`. See `Nudge.Vpx.Settings.VpxIniFile`.

## Table files

`.vpx` is a **Microsoft OLE Compound Document** (magic `D0 CF 11 E0 A1 B1 1A E1`), NOT a zip.
Storages: `GameStg` and `TableInfo`. Typical size 2–460 MB in the real, mixed-quality table
collection used to validate Phase 2 — mostly images and sound sitting in `GameStg`.

- `TableInfo` holds author metadata — **frequently blank or inherited-and-wrong**, because most
  tables are mods of mods. Filename parsing is often *more* accurate. **Directly confirmed**: a
  real file named `Breaking Badv2.vpx` reports `TableName = "Strange Science"` internally — the mod
  chain moved on and the metadata never caught up. Nudge's table reader treats this as an ordinary,
  expected case, not an anomaly: it prefers the filename for the display title when the two
  disagree, but keeps both values and records the disagreement as evidence.
- **The `TableInfo` stream encoding is plain UTF-16LE text, with no length prefix and no null
  terminator** — the stream's own byte length is exactly `2 × character count`. Verified against 33
  real table files spanning 12 different table authors/mod chains during Phase 2 development; the
  encoding held with zero exceptions. See `Nudge.Vpx.TableFiles.OleTableInfoReader`.
- `ReleaseDate` has **no consistent format**. Real values observed: `"01/04/22"`, `"09.07.2025"`,
  `"7/24/2021"`, `"june 2018"`, `"2-4-2022"`, `"December 2019"`, `"July 17, 2021"`. Never parsed
  into a `DateTime` — kept as free text.
- The `.vpx` filename convention `"Title (Manufacturer Year).vpx"` is followed by roughly **half**
  of real files. `BlackKnight2000(Williams 1989).vpx` and
  `CreatureFromTheBlackLagoon(Bally 1992)_1.3.vpx` match cleanly; `Batman66.vpx` and
  `AttackfromMarsMidway 1995v600.vpx` have no parseable structure; `Albator the movie (VR ROOM).vpx`
  has parentheses that are not a manufacturer/year pair at all. `Nudge.Vpx.TableFiles.TableFilenameParser`
  is built to produce an honest partial or empty result for all three shapes, never a wrong guess.
- **The PinMAME ROM name is not metadata.** It is a `cGameName` assignment inside the table's
  VBScript in `GameStg`. Extracting it requires parsing the script. **Not yet implemented** —
  deliberately deferred past the initial Phase 2 pass as a second-pass background operation, per
  the plan below.
- A fast scan should read only the small `TableInfo` streams. Never load a whole file. Phase 2's
  reader does exactly this — confirmed by running it against 33 real files up to 460 MB each
  without reading their `GameStg` storage at all.

## Surrounding ecosystem

- **VPinMAME** is a registered COM object. All VPX installs on a machine **share one
  registration**. ROM folder comes from
  `HKCU\Software\Freeware\Visual PinMame\globals\rompath` (fallback `HKLM`). ROMs are `.zip`
  files, kept zipped, named after the ROM name. Nudge's registry discovery layer reads this key to
  find installations — see `Nudge.Vpx.Discovery.RegistryCandidateProvider`.
- **B2S**: `<TableName>.directb2s` next to the table. B2S Server is a COM object in the `Tables`
  folder. In 10.8.1 this is becoming an in-process plugin (`B2S Legacy`), enabled via the live UI
  plugin manager. **Health rules must be version-aware** or they will produce confident wrong
  answers — relevant from Phase 7 onward, not Phase 1.
- **Scores**: VPX does not report scores. Community reads PinMAME NVRAM via PINemHi. Nudge does
  not and will not claim score tracking.

## Observed on the maintainer's real machine (Baller Installer)

Baller Installer 10.8.0 Final build 2058 was the design target, but the maintainer's actual
install (found via registry discovery during Phase 1 testing) carries considerably more than the
minimal Baller layout described in early planning:

- `VPinballX.exe`, `VPinballX64.exe` — DirectX 9, classified Medium confidence (filename says DX9,
  version resource doesn't explicitly name a flavor, so nothing corroborates beyond the name)
- `VPinballX_GL64.exe` — OpenGL x64, High confidence, reports OpenVR
- **No BGFX build** — confirms the AGENTS.md note that Baller users are on the OpenVR/SteamVR path
  by default, not OpenXR
- `VPinball921.exe`, `VPinball995.exe`, `VPinball99_PhysMod5_Updated.exe` — three separate VP9-era
  executables, all correctly classified `VP9Legacy` at High confidence
- `VPinballX106.exe`, `VPinballX107_32bit.exe`, `VPinballX107_64bit.exe`, `VPinball8.exe` — older
  or unofficial builds Nudge does not recognise; correctly reported `Unknown` rather than guessed
- A `VPinMAME` subfolder containing ten unrelated executables (`PinMAME.exe`, `dmdext.exe`,
  `Setup.exe`, etc.) — correctly rejected as an installation root by the validator, since none of
  them are Visual Pinball

This is good validation that the "never guess, report Unknown" rule matters in practice: a real
install has considerably more noise than the clean four-executable layout used in early planning.

## Where sources conflicted

None yet. If a future Visual Pinball release changes something documented here, record the
conflict and which version it applies to rather than overwriting this file silently.
