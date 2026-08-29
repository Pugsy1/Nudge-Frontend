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
wordmark now uses the supplied logo PNG (`Assets/Nudge Logo Words.png`), recolored per theme.

**Custom window chrome** — superseded during Phase 4 UI work: `MainWindow.xaml` now uses
`WindowStyle="None"` with a full custom `WindowChrome` (themed minimize/maximize/close, true
borderless fullscreen launch). See `docs/UI-SESSION-HANDOFF.md` for details. The line below
describing Phase 1 as shipping with the standard title bar is accurate for what Phase 1 itself
built; it was superseded later, not revised retroactively.

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

## Phase 3 — Database and library scanner

**Status: functionally complete for its stated scope (schema, EF Core, migrations, repositories,
scanner), headless. Wired into Nudge.App's startup (database created and migrated automatically),
but nothing in the UI triggers a scan or displays scanned tables yet - that's Phase 4.**

### What's built

**`Nudge.Data`** - a new project. SQLite via EF Core 10 (the locked decision), with:

- `TableEntity` / `NudgeDbContext`: one `Tables` table, keyed by an auto-increment `Id`, with a
  unique index on `(InstallationId, FilePath)` and a plain index on `DisplayTitle` (added now,
  ahead of the grid needing to sort/filter by it in Phase 4, rather than as a follow-up migration).
- `TableRepository : ITableRepository` (the interface lives in `Nudge.Core`, so nothing outside
  `Nudge.Data` knows EF Core exists). Converts between the I/O-free `VpxTableFile` and the
  persisted `TableEntity` - `FilenameHints.Tags` and `DetectionEvidence` are stored as JSON
  columns, since neither has independent identity worth a related table.
- **Batched writes**: `UpsertManyAsync` does one query to find every existing row a batch might
  touch, then one `SaveChangesAsync` for the whole batch - one transaction per few hundred rows,
  per AGENTS.md's performance budget, not one per row.
- An `InitialCreate` migration, generated and applied for real (see verification below).
- A security fix along the way: EF Core 10.0.0 pulls in `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 and
  `System.Security.Cryptography.Xml` 9.0.0, both with known high-severity CVEs. Pinned both up to
  patched versions via direct `PackageReference` overrides in `Nudge.Data.csproj` (a direct
  reference wins over EF Core's transitive one). Worth re-checking next time EF Core itself is
  upgraded, in case a newer EF Core release fixes this upstream.

**`Nudge.Library`** - a new project, holding `VpxLibraryScanner : IVpxLibraryScanner`:

- Walks a folder recursively for `*.vpx` files.
- **Incremental scanning**: compares each file's current size and last-write time against what was
  stored last time (`ITableRepository.GetFingerprintsAsync`); unchanged files are not re-read at
  all. Confirmed for real (below): a repeat scan of 61 real tables dropped from 2.6s to 49ms.
- Reads every new-or-changed file through Phase 2's `ITableFileReader`, batches results, and
  removes database rows for files that vanished since the last scan
  (`ITableRepository.DeleteMissingAsync`).
- Reports `IProgress<ScanProgress>` as it works, for a future progress bar.
- One failed file does not stop the scan - it's counted and its path recorded, and everything else
  keeps going.

**Verified two ways**, matching the pattern from Phases 1 and 2:

1. **Tests**: 8 in `Nudge.Data.Tests` against a real, in-memory SQLite database (not EF Core's
   separate InMemory provider, which doesn't enforce real constraints or behave identically to a
   real relational engine) - round-tripping tables, JSON columns, batch upserts, per-installation
   scoping, and deletion. 10 in `Nudge.Library.Tests`, with every real production class wired
   together (the real `VpxTableFileReader` reading real synthetic OLE files through a mock
   filesystem, the real `TableRepository` against real in-memory SQLite) - covering new/changed/
   unchanged/deleted files, subfolders, non-`.vpx` files being ignored, and one bad file not
   stopping the rest of the scan. 129 tests pass across the whole solution.
2. **A real, on-disk SQLite database**, scanned against the maintainer's actual 61-table folder
   (grown since Phase 2's 33-file check) via a throwaway harness built from the real production
   classes: first scan found and stored all 61 with zero failures in 2.6 seconds; a second scan
   against the same folder correctly skipped all 61 as unchanged, finishing in 49ms. Also verified
   end-to-end inside the real running app: `nudge.db` is created and migrated automatically on
   startup, and the redaction rule still holds (`%LocalAppData%` paths in the log show `<user>`,
   not the real Windows username).

### What's deliberately not built (Phase 3 scope)

- **Nothing in the UI triggers a scan or shows scanned tables.** `AddNudgeData` / `AddNudgeLibrary`
  are registered in `Nudge.App`'s DI container and the database is migrated at startup, but the
  setup screen's behaviour is completely unchanged. That wiring is Phase 4's job.
- **No grouping or duplicate detection** across tables (e.g. the same table appearing under two
  different filenames). That's `Nudge.Library`'s "grouping" responsibility, not yet built.
- **No health checking or import.** Also `Nudge.Library`'s job, in later phases.
- `ITableRepository` and `NudgeDbContext` are registered with EF Core's default Scoped lifetime.
  Nothing resolves them outside of `MigrateNudgeDatabaseAsync`'s own scope yet - Phase 4's grid
  view model will need to either be Scoped itself or create its own scope when it starts using
  the repository.

### Known limitations

- **The scanner's incremental fingerprint is size + last-write time only**, per AGENTS.md's stated
  approach. It does not hash file contents, so a file rewritten with the exact same size and
  write-time (vanishingly unlikely in practice, but not impossible with certain backup/sync tools)
  would be wrongly skipped. Matches the documented design; flagged here so it isn't forgotten.
- ~~No concurrent-scan protection.~~ **Fixed.** `VpxLibraryScanner` now holds a per-installation
  semaphore so a second `ScanAsync` call for an installation already being scanned waits for the
  first to finish rather than racing it - see the commit that closed this out. Scans of different
  installations are unaffected by each other's gate. Covered by
  `Concurrent_scans_of_the_same_installation_are_serialized_not_racing` in
  `Nudge.Library.Tests`.
- ~~`VpxLibraryScanner` captured a Scoped `ITableRepository` in a singleton constructor.~~
  **Fixed.** It now takes an `IServiceScopeFactory` and resolves a fresh `ITableRepository` (and
  therefore a fresh `NudgeDbContext`) inside every `ScanAsync` call, scoped and disposed with that
  call. `IVpxLibraryScanner`'s public contract - and therefore every caller, including whatever the
  Phase 4 UI resolves - is unchanged; only the scanner's own constructor and internals changed. No
  DI registration changes were needed: `IServiceScopeFactory` is provided automatically by the
  built-in container. Confirmed via all 11 `Nudge.Library.Tests` still passing, including the
  concurrency-gate test above, using a small fake `IServiceScopeFactory` in the test that hands out
  a fresh in-memory-SQLite-backed repository per scope, the same shape `AddNudgeData` gives the
  running app.
- **Migrations have been generated and applied against a real file once**, in the running app and
  in the throwaway validation harness, but not yet exercised across an upgrade path (an existing
  `nudge.db` from an older schema version being migrated forward) - there's only ever been one
  migration so far.

## Phase 5 — Launch engine (backend only; table detail screen not yet built)

**Status: the launch engine itself is functionally complete and verified against a real
installation. The UI half of Phase 5 - a table detail screen with a Play button - has not been
started; this section covers only `Nudge.Vpx`/`Nudge.Core`.**

### What's built

**`ILaunchEngine`** (`Nudge.Vpx.Launching.LaunchEngine`) launches a table and waits for Visual
Pinball to exit: builds `-Play "<table path>"` via `ProcessStartInfo.ArgumentList` (so a path with
spaces or quotes can't be misparsed), runs it through `IProcessRunner` (a thin, fake-able wrapper
over `Process`), and returns the exit code and duration. A non-zero exit code is still a
*successful* launch - Visual Pinball ran and exited, and interpreting what a crash means is the
health system's job in a later phase.

**`VpxInstallation.BestDesktopExecutable`** decides which executable to launch: the most modern
recognised build available, currently BGFX then DirectX 9, preferring x64 over x86 within a flavor.
Two flavors are excluded outright, not just ranked low:
- `VpxFlavor.VP9Legacy` plays `.vpt` files, not `.vpx` - launching it against a scanned `.vpx` table
  would be silently wrong, not a loud failure.
- `VpxFlavor.OpenGL` - **discovered during this phase's own verification, not anticipated**: a real
  launch of `VPinballX_GL64.exe` against the maintainer's test installation, with a plain `-Play`
  and no VR flag of any kind (there is no VR flag), came up in VR anyway. The OpenGL build
  autodetects a SteamVR install and engages VR with no way for Nudge to know in advance, so it is
  never chosen for a Desktop launch until Phase 6 gives Nudge its own VR profile / `-Ini` control.
  See docs/RESEARCH-NOTES.md. An OpenGL-only installation currently reports no Desktop build
  available at all, rather than risk silently launching VR on the user.

### What's deliberately not built (Phase 5 scope)

- **No UI uses this yet.** No table detail screen, no Play button, no "now playing" state. That's
  the other half of Phase 5.
- **No VR launch path.** Deliberately deferred to Phase 6, which is also where the maintainer's
  stated direction for the eventual UI - default Play to DirectX9, with a separate explicit way to
  choose VR, exact interaction undecided - is recorded (docs/RESEARCH-NOTES.md).
- **No crash interpretation.** A non-zero exit code is recorded on `LaunchOutcome` but nothing reads
  it yet - that's the health system, Phase 7.

### Verified two ways

1. **12 tests** (`VpxInstallationTests`, `LaunchEngineTests`) against a faked `IProcessRunner` and
   `MockFileSystem` - executable selection across every flavor/architecture combination, argument
   construction, working directory, a non-zero exit code still counting as success, and launch
   failures (missing table file, no desktop build available, process failed to start) reported as
   `Result` failures rather than exceptions. 124 tests pass in `Nudge.Vpx.Tests` overall.
2. **A real launch against the maintainer's actual test installation** (`D:\Frontend TEST\Visual
   Pinball`), via a throwaway harness built from the real production classes: confirmed
   `VPinballX_GL64.exe` actually started as a real OS process with the expected PID, confirmed
   cancelling the engine's wait does not kill that process (Nudge does not own the user's play
   session once Visual Pinball is running - AGENTS.md section 6), and confirmed the process could be
   torn down cleanly afterward. This same run is what surfaced the OpenGL/VR finding above.

## PinMAME ROM name extraction (headless; not wired into the scanner)

**Status: functionally complete for its stated scope, verified against real files. Deliberately not
wired into `IVpxLibraryScanner` or persisted to the database yet** - AGENTS.md section 4.5 frames
this as a second-pass background operation separate from the fast scan, and running it on every
scanned file (reading the much larger `GameStg` storage, not just the small `TableInfo` streams)
is a real performance-budget question deserving its own explicit decision, the same way Phase 4/5's
scope changes did.

### What's built

- **`Nudge.Vpx.TableFiles.GameDataScriptReader`** extracts a table's VBScript source from
  `GameStg\GameData`'s `CODE` BIFF record. The binary format is not documented by vpinball itself;
  it was cross-checked against the open-source `vpin`/`vpxtool` projects and independently verified
  against four real table files - see docs/RESEARCH-NOTES.md for the full format and sourcing.
- **`Nudge.Vpx.TableFiles.RomNameParser`** searches that script text for a top-level `cGameName`
  assignment. Confidence: High for exactly one uncommented assignment, Medium when several
  different uncommented assignments exist (the first is used), Unknown when nothing is found -
  including tables that assign the ROM name conditionally at runtime, which Nudge does not attempt
  to evaluate.
- **`IRomNameReader`** (`Nudge.Core.Abstractions`) is the public contract combining the two, mirroring
  how `ITableFileReader` combines `IOleTableInfoReader` and `ITableFilenameParser`.

### A real OpenMcdf gotcha hit while building the tests for this

Writing more than one stream into the same OLE storage using C#'s brace-less `using` *declaration*
(rather than an explicit `using (...) { }` *block*) silently drops the contents of every stream in
that storage - they all read back at 0 bytes, no exception thrown. `Nudge.TestSupport.SyntheticVpxFile`
had exactly this bug the moment a second stream (`GameData`, alongside the existing `Version`) was
added to its synthetic `GameStg` storage: `Version`'s `using` declaration kept it open until the end
of the method, so by the time `GameData` was created and written, both ended up empty. Fixed by
giving each stream its own explicit block so it is disposed - and actually flushed - before the next
one is created. Existing single-stream-per-storage usage (`TableInfo`'s fields, each written inside
its own `WriteIfNotNull` method call) was never affected, which is why this was not caught earlier.

### Verified two ways

1. **16 tests** (`GameDataScriptReaderTests`, `RomNameParserTests`, `RomNameReaderTests`) against
   `MockFileSystem` and a real, parseable synthetic `.vpx` file (`SyntheticVpxFile`, now able to
   write a real `GameData` BIFF stream) - covering a clean single match, commented-out alternatives
   correctly ignored, multiple uncommented matches, a conditionally-assigned ROM name correctly
   reported as not found, missing/malformed records, and a file that isn't a readable `.vpx` at all
   failing outright. 140 tests pass in `Nudge.Vpx.Tests` overall.
2. **Directly verified against four real, independently-authored table files** from the maintainer's
   test collection via a throwaway harness: `Medieval Madness` → `mm_109b` (correctly skipping three
   commented-out alternatives), `BlackKnight2000` → `bk2k_l4`, `Nudge Test and Calibration` →
   `dof_test`, and `Twilight Zone` → correctly not found (conditionally assigned, as above).

## Artwork (headless; UI not wired up yet)

**Status: functionally complete for its stated scope, verified against the real live vps-db dataset.
No UI displays any of this yet - that's a separate piece of work for the UI session, using the
interface and setting reported below.**

### What's built

- **`IArtworkProvider`** (`Nudge.Core.Abstractions`) / **`ArtworkImage`** (`Nudge.Core.Models`) - the
  public contract. `GetArtworkAsync(VpxTableFile, CancellationToken)` returns
  `Result<ArtworkImage>`; failure (no match, no image, network error, setting off) is always the same
  ordinary outcome, never an exception.
- **A new project, `Nudge.Media`**, implementing it against
  [vps-db](https://github.com/VirtualPinballSpreadsheet/vps-db), the community VPX metadata/artwork
  dataset - see docs/RESEARCH-NOTES.md for why this was chosen over every local source that was
  investigated (a PinballX/PinballY-style media folder does not apply to a bare VPX install; VPX's
  own embedded images have no reliable way to identify which one is cover art; `.directb2s` backglass
  extraction was fully verified but not used, per explicit instruction to prefer network artwork).
  - `VpsDbIndex` downloads and caches `db/vpsdb.json` (updated daily upstream, so cached for 24h;
    falls back to a stale cache rather than nothing when the network fails).
  - `VpsDbMatcher` matches a table by **token-set** comparison (camelCase- and digit-aware word
    splitting, stopwords dropped, one side's words allowed to be wholly contained in the other's when
    it has 2+ significant words), disambiguated by manufacturer then year - measured at 50/61 (82%)
    against the maintainer's real collection, up from 37/61 (61%) for plain exact-string matching.
    See docs/RESEARCH-NOTES.md for the full before/after and the two known remaining edge cases.
  - `ImageResizer` (`SixLabors.ImageSharp`, pinned to 3.1.12 for the same license reason
    FluentAssertions is pinned to 7.2.0 - see docs/RESEARCH-NOTES.md) decodes WebP/PNG/JPEG and
    downscales to at most 480px on the long edge, never upscaling.
  - `ArtworkCache` permanently caches the resolved, resized PNG per table (keyed by a hash of the
    table's path) - a table is never fetched from the network twice.
  - `VpsDbArtworkProvider` ties it together: cache first, then the `FetchArtworkFromInternet`
    setting, then the index match, then a rate-limited (300ms minimum between requests), timed-out
    (15s) download.
- **`NudgeSettings.FetchArtworkFromInternet`** (`Nudge.Core.Models`, default `false`) - the opt-in
  gate. Off by default because this is the only thing Nudge does that talks to the network at all.

### What's deliberately not built

- **No UI uses this yet.** No image renders anywhere; tiles still show the placeholder initial. The
  composition root also does not call `AddNudgeMedia()` yet - see "For the UI/composition session"
  below.
- **No local artwork source at all** - not `.directb2s` (built and verified, but not wired in), not
  embedded `GameStg` images (investigated, rejected as unreliable), not a media folder (does not
  apply). Artwork is 100% network-sourced right now; a table gets nothing if vps-db has no match or
  the setting is off, by design (per the maintainer: don't chase artwork for tables it doesn't come
  to naturally - a manual per-table picker is the intended answer for those, not yet built).
- **No manual "pick your own poster" override.** Mentioned by the maintainer as the intended way to
  handle tables artwork can't be found for automatically - a `Nudge.Data`/UI feature for later.

### For the UI/composition session

- Call `services.AddNudgeMedia(artworkCacheDirectory)` in the composition root (`App.xaml.cs`) after
  `AddNudgeVpx()`, the same pattern `AddNudgeData(databasePath)` already uses.
  `artworkCacheDirectory` should be something like
  `Path.Combine(ResolveDataDirectory(environment), "artwork")`.
- Resolve `IArtworkProvider` and call `GetArtworkAsync(table)` per tile; treat `Result.Failure` as
  "show the placeholder", not an error. Nothing here blocks a calling thread, so this is safe to
  await per-tile as tiles come into view.
- The opt-in setting is `NudgeSettings.FetchArtworkFromInternet` (default `false`) - needs a toggle
  somewhere in Settings.

### Verified two ways

1. **34 tests** (`Nudge.Media.Tests`) against a faked `HttpMessageHandler`, `MockFileSystem`, and
   real generated images through the real `ImageResizer` - matching/disambiguation (including the
   token-set rules: camelCase/digit splitting, stopwords, subset matching, the short-title guard),
   cache-hit short-circuiting, the setting gate, stale-cache fallback on a network failure, download
   failure handling, and upscale-never/downscale-to-480px resizing. Two tests caught real bugs
   before they shipped: ImageSharp's `ImageFormatException` does not derive from
   `InvalidOperationException` (the provider's catch clause was silently wrong for a corrupt network
   image until the test exposed it), and the first tokenizer pass turned "BlackKnight2000" into
   "Black" + "Knight2000" rather than "Black"/"Knight"/"2000", silently regressing a match that
   worked under the old plain-string comparison.
2. **Verified live against the real vps-db dataset and network, twice**: first end-to-end for three
   tables (downloaded the real index, matched by title/manufacturer/year, downloaded each match's
   real image, decoded and resized through the real pipeline, visually confirmed a correct Medieval
   Madness playfield screenshot); then again at scale, running the real scanner's table reader plus
   the real matcher against **all 61 real table files** in the maintainer's test collection - measured
   50/61 (82%) matched against the live index, up from 37/61 (61%) for the original plain
   exact-string matcher. See docs/RESEARCH-NOTES.md for the categorised misses.
