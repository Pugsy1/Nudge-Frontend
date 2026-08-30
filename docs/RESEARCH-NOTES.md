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

**Maintainer-confirmed, treat as settled project guidance**: the OpenGL build (`VPinballX_GL64.exe`
etc.) auto-engages VR on every user's machine it's been observed on, not just the maintainer's own -
directly confirmed when Phase 5's launch-engine verification launched it with a plain `-Play`
(no `-Ini`, no VR flag of any kind - there is no VR flag) and it came up in VR anyway. Until Phase 6
gives Nudge its own VR profile / `-Ini` control, **Nudge treats OpenGL as unusable for a Desktop
launch and never selects it for one** - see `VpxInstallation.BestDesktopExecutable` in
`Nudge.Core`, which excludes `VpxFlavor.OpenGL` from Desktop selection the same way it excludes
`VpxFlavor.VP9Legacy`. An OpenGL-only installation currently reports no Desktop build available
rather than risk silently launching VR on the user.

The mechanism for "PLAY IN VR" will be:

```
VPinballX_GL64.exe -Ini "<Nudge's own VR profile.ini>" -Play "<table.vpx>"
```

Nudge will own that ini file, inside Nudge's own data directory, when this is built in Phase 6.

**Maintainer's stated direction for Phase 6's UI** (not designed yet, noted here so it isn't lost):
the frontend should always default to the plain DirectX9 build (`VPinballX.exe`) for a normal Play
action, with a separate, explicit way to launch VR instead - possibly a one-click switch or a
hover-revealed option on the table tile. Exact interaction still undecided; whoever designs Phase
6's UI should treat this as the starting brief, not a locked decision.

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
  VBScript in `GameStg`. **Implemented** as a deliberately separate, second-pass reader -
  `Nudge.Vpx.TableFiles.GameDataScriptReader` (extracts the script) plus `RomNameParser` (searches
  it) - not wired into the fast library scan; see docs/IMPLEMENTATION-STATUS.md for what that means
  in practice.
  - `GameStg\GameData` is a sequence of **BIFF-style tagged records**: a 4-byte little-endian
    length, the 4-byte tag, then the payload, ending in an `ENDB` record. For an ordinary record
    that length covers the tag *plus* its payload — e.g. a float record is `8` (4 tag + 4 value).
  - **The `CODE` record, which holds the script, is framed differently, and this is the one detail
    worth not rediscovering.** Its record length is only ever `4` — the tag alone — and the script's
    own 4-byte length follows immediately *after* the tag, outside that record length. (`vpin` calls
    this shape "tagged string with no size".) The text is UTF-8 when valid, Latin-1 otherwise.
    **Confirmed against a real file**: Medieval Madness's `CODE` record sits 258 records in, reads
    `recordLength=4`, and is followed by a 211,629-byte script.
  - **This format is not documented anywhere in vpinball's own repository or shipped docs.** It was
    cross-checked against the open-source community projects
    [francisdb/vpin](https://github.com/francisdb/vpin) and
    [francisdb/vpxtool](https://github.com/francisdb/vpxtool) (which read/write real `.vpx` files
    this same way), then verified here against real, independently-authored table files.
  - **A caution, learned the hard way here.** The first implementation assumed `CODE` was framed
    like every other record (length covering tag + payload) and the synthetic test fixture was
    written to match that same assumption. Sixteen unit tests passed against it while the reader
    found *nothing at all* on all 61 real tables — the fixture and the reader simply agreed with
    each other. Anything parsing this format must be checked against real files on disk, not only
    against a fixture this repository generates.
  - The `cGameName` search convention, confirmed against those same four real files: a top-level
    `Const cGameName = "romname"` (or without `Const`) assignment somewhere in the script. Real
    tables are messier than that in practice - **Medieval Madness** carries four different
    `cGameName` lines, three commented out with only one live; `RomNameParser` correctly finds only
    the live one because commented lines never start with `Const`/`cGameName` at all. **Twilight
    Zone** assigns `cGameName` conditionally inside a `Select Case` block with no single top-level
    constant; Nudge does not evaluate script logic, so this correctly reports "not found" rather
    than guessing a branch.
- A fast scan should read only the small `TableInfo` streams. Never load a whole file. Phase 2's
  reader does exactly this — confirmed by running it against 33 real files up to 460 MB each
  without reading their `GameStg` storage at all.
- **ROMs are looked up by name in VPinMAME's registered `rompath`**, as `<rompath>\<romname>.zip`
  (they stay zipped). `Nudge.Vpx.Roms.RomAvailabilityChecker` reads that path from
  `HKCU\Software\Freeware\Visual PinMame\globals\rompath`, falling back to `HKLM`. Because VPinMAME
  is registered machine-wide, this resolves to whichever install registered last — on the
  maintainer's machine that is the real Baller install's `roms` folder, even when the tables being
  checked come from the separate test install. That is correct: it is the same folder Visual Pinball
  itself would load ROMs from.
- **Measured against the maintainer's real 61-table collection**: 36 tables name a ROM that is
  installed, 19 name a ROM that is **not** installed, and 6 name no ROM at all (originals and
  test/calibration tables). A cold pass over all 61 took ~2.9s — roughly 47s per 1,000 tables, which
  is why script/ROM reading is kept as a second pass rather than folded into the fast scan, whose
  whole budget for 1,000 tables is 60s.

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

## Artwork: local sources investigated, network source chosen

Researched for `IArtworkProvider` (`Nudge.Core.Abstractions` / `Nudge.Media`). Every local option was
checked against the maintainer's real installations before deciding none of them were usable as the
primary source, then a network source was chosen and verified live.

- **A PinballX/PinballY/HyperPin-style media folder** (`Media\<System>\Wheel Images\<Game>.png` etc.,
  confirmed via [PinballY's own docs](http://mjrnet.org/pinscape/downloads/PinballY/Help/DirectoryInfo.html))
  does not apply here at all: that folder lives inside a *separate frontend's own install directory*,
  not inside a VPX installation, and importing from another frontend's media library is already its
  own later phase - AGENTS.md's phase table, Phase 10 ("Frontend media import"). Checked both real
  installations on this machine directly: neither has one, and neither has loose image files sitting
  next to any `.vpx` either.
- **`GameStg`'s own embedded images, as a last-resort local fallback** - investigated and **rejected**.
  VPX has a field seemingly built for exactly this (`SSHT`/`screen_shot` in `GameData`, confirmed via
  [francisdb/vpin](https://github.com/francisdb/vpin)'s source), but it is empty on every real table
  checked. Without it, there is no reliable way to identify which of a table's dozens of embedded
  images (Medieval Madness has 76) is "the" cover art rather than a VR floor texture, a color-grade
  LUT, or a ball decal - one embedded image only looked plausible because its author happened to name
  their source folder "Backglass". Picking a heuristic (largest image, first image, name contains
  "wheel"/"backglass") would sometimes silently return a texture as if it were real artwork - exactly
  the kind of guess this project does not present as fact. Not implemented.
- **`.directb2s` backglass files** - fully implemented and verified, then **explicitly overridden**:
  the maintainer asked for network-sourced artwork instead of local files as the metadata/thumbnail
  source. The format itself (XML, `Images/BackglassImage/@Value` holding base64 PNG bytes with
  embedded `&#xD;&#xA;` line-break entities that `Convert.FromBase64String` tolerates natively) was
  confirmed working end-to-end against two real `.directb2s` files, producing correctly-decoded real
  backglass images. Not wired into `IArtworkProvider` per that instruction, but the finding is
  recorded here in case local artwork is revisited later.
- **The network source: [vps-db](https://github.com/VirtualPinballSpreadsheet/vps-db)**, the
  community VPX metadata/artwork dataset already used by PinUP Popper, PinballX and similar
  frontends. **Has no license anywhere** - no `LICENSE` file, no README, GitHub's API reports
  `"license": null` - which under default copyright means no formal reuse permission is granted,
  despite being the de facto standard for this exact category of tool. The alternatives checked
  (`vpdb.io`, `opdb.org`) had no clearer terms and no confirmed VPX-specific artwork either. Used
  anyway on the maintainer's explicit instruction, accepting the same informal-but-widely-used
  posture every comparable open-source VPX tool already takes toward this source.
  - Schema (confirmed against the live 7 MB `db/vpsdb.json`, 2,568 entries at the time of checking):
    a flat JSON array of `{ id, name, manufacturer, year, tableFiles: [...], b2sFiles: [...],
    wheelArtFiles: [...] }`. `tableFiles[].imgUrl` and `b2sFiles[].imgUrl` are direct, fetchable
    `https://virtualpinballspreadsheet.github.io/vps-db/img/*.webp` URLs; `wheelArtFiles` almost
    always link out to a download *page* (vpuniverse.com, vpforums.org) rather than hosting an
    image directly, so it is not used. `tableFiles` (playfield screenshot) is preferred over
    `b2sFiles` (backglass) only because it is more consistently present in the real data.
  - **Matching started as simple normalised-equality on title** (strip everything but
    letters/digits, lowercase), disambiguated by manufacturer then year. **Measured against all 61
    real tables in the maintainer's test collection, this only matched 37 (61%)** - real table
    titles are messier than a clean database entry in ways plain string equality can't bridge:
    - A "VR ROOM"/"VR Room" naming prefix some VR-conversion authors add, not part of the real game
      title (7 of the 24 original misses): `"VR ROOM Attack from Mars"` vs vps-db's `"Attack from
      Mars"`.
    - Edition suffixes: `"Attack from Mars LE"`, `"Game of Thrones LE"`, `"X-Men LE"` vs a base
      entry with no suffix.
    - Concatenated, separator-free titles from a filename with no spaces at all:
      `"BatmanDarkKnight"` vs vps-db's `"Batman: The Dark Knight"`.
  - **Upgraded to token-set comparison** in `VpsDbMatcher`: split each title into significant words
    (camelCase-aware - `"BatmanDarkKnight"` splits the same as a spaced title - and digit-aware -
    `"BlackKnight2000"` splits into `"Black"`/`"Knight"`/`"2000"`, not `"Black"`/`"Knight2000"`),
    drop stopwords (`the`, `a`, `an`, `of`, `and`, `in`), and treat two titles as matching when their
    word sets are equal **or** one is wholly contained in the other. This fixes all three patterns
    above without special-casing any of them - "VR ROOM Attack from Mars" and "Attack from Mars"
    simply share the token set `{attack, from, mars}` once "VR ROOM" or nothing extra is left over.
    **A single-word token set only ever counts as an exact match, never as "contained in" something
    longer** - the deliberate guard that stops a table simply called `"Mars"` from subset-matching
    into `"Attack from Mars"`. **Re-measured against the same 61 tables: 50 matched (82%)**, using
    the real production scanner's table reader plus the real matcher, no synthetic shortcuts.
  - **A regression caught by re-measuring rather than assuming the fix was clean**: the first
    tokenizer pass added camelCase splitting but not letter/digit splitting, which silently broke a
    table that matched fine under the *old* plain-string comparison - `"BlackKnight2000"` tokenized
    to `{black, knight2000}` (the year stayed glued to the word before it) instead of `{black,
    knight, 2000}`, sharing no token with vps-db's separately-tokenized entry. Fixed by adding an
    explicit letter/digit boundary rule alongside the camelCase one; a regression test
    (`Splits_a_trailing_year_glued_directly_onto_a_word_with_no_separator`) pins this down.
  - **Two remaining misses understood but not chased further**, to avoid loosening the short-title
    guard on uncertain benefit: `"BatmanDarkKnight"` (Stern, 2008) vs vps-db's real entry, simply
    `"Batman"` (also Stern, 2008) - manufacturer and year corroborate the match, but the title guard
    correctly refuses a single-word subset match without that extra step, which was not built; and
    `"Bride Of Pinbot"` vs vps-db's `"Pin-bot"` (hyphenated) - the hyphen makes vps-db's tokenizer
    output split into `pin`/`bot` while the unhyphenated form stays one token `pinbot`, so the sets
    don't align. The rest of the 11 remaining misses are tables not realistically expected to be in
    vps-db at all (physics test tables, a screen-calibration table, one very new/obscure custom) or a
    pre-existing Nudge data-quality issue unrelated to matching (a table's internal metadata still
    claims a stale title from an earlier version of the mod, exactly the kind of staleness
    docs/RESEARCH-NOTES.md's `TableInfo` notes already describe).
  - **Verified live, end to end**: downloaded the real index, matched three real tables from the
    maintainer's collection (Medieval Madness, Black Knight 2000, Twilight Zone) by title,
    manufacturer and year, downloaded each match's real `.webp` image over the network, decoded and
    resized each through the real `ImageResizer`, and visually confirmed the Medieval Madness result
    was a correct, real playfield screenshot.
  - **WebP decoding**: `System.Drawing.Common`/GDI+ does not reliably decode WebP even on Windows
    versions with WIC-level WebP support - the codec is often not installed, and GDI+ frequently
    does not surface it even when it is. Used `SixLabors.ImageSharp` instead (pure managed code, no
    OS codec dependency, confirmed WebP support in its decoder list at runtime), **pinned to 3.1.12**
    - the same reasoning as FluentAssertions being pinned to 7.2.0: ImageSharp 4.0.0 introduced the
    "Six Labors Split License" with a build-time license key requirement for direct dependencies;
    3.1.x is the last Apache-2.0-only line with no key.
  - **A real exception-hierarchy gotcha, caught by a test rather than assumed**: ImageSharp's
    `ImageFormatException` (thrown for a corrupt/unrecognised image) does **not** derive from
    `InvalidOperationException`, despite looking like it should - it derives directly from
    `Exception`. A test asserting the wrong exception type caught this before it shipped as a real
    gap in `VpsDbArtworkProvider`'s error handling (an unhandled exception on a bad network image,
    rather than the intended graceful "not found").

## A second artwork source was requested: Google/IPDB scraping is not possible, an official API is

The maintainer asked for a second source, specifically "scrape from Google" for pinball posters.
Checked before writing any code, per the same rule vps-db was held to:

- **Google Search/Images**: Google's Terms of Service explicitly prohibit automated/programmatic
  access to Search, and Google actively enforces this with bot detection and CAPTCHAs. This is a
  fundamentally different situation from vps-db (no stated terms either way, a gray area) - this is
  an explicit, active prohibition. **Not built, and won't be**, regardless of instruction to
  proceed: building a scraper here would mean defeating Google's bot detection, which is a hard
  line, not a judgement call weighed against convenience.
- **IPDB (Internet Pinball Database)**: checked as a real-flyer/poster alternative. IPDB's own
  `robots.txt` was recently updated to **block all crawlers**, stated by the maintainers to be a
  direct response to being overwhelmed by AI-scraper traffic. As explicit a "do not scrape us"
  signal as a site can give without a formal terms page. Not built.
- **The legitimate path taken instead: Google's own Custom Search JSON API**
  (`developers.google.com/custom-search`), an official, sanctioned, documented API - not a scrape of
  anything. Requires the user's own free API key (Google Cloud Console) and a Programmable Search
  Engine ID ("cx", from programmablesearchengine.google.com) - Nudge cannot provision either on the
  user's behalf, and does not try to. Free tier: 100 queries/day at the time this was written, a
  real published quota rather than an undocumented rate Nudge has to guess at and stay under.
  - Endpoint and schema confirmed against Google's own published REST reference
    (`developers.google.com/custom-search/v1/reference/rest/v1/cse/list` and `.../Search`):
    `GET https://customsearch.googleapis.com/customsearch/v1?key=&cx=&q=&searchType=image`. For an
    image search specifically, the **top-level `link` field is the direct image file URL** - not
    `image.contextLink` (the page the image was found on) or `image.thumbnailLink` (a small
    preview), which is an easy field to pick wrong without checking the docs closely.
  - **Not verified against a real live call** - unlike every other network integration in this
    project, which was checked against real data before being called done. This one genuinely
    cannot be: it needs a user-supplied API key and search engine ID that do not exist yet. Built
    and unit-tested against the documented contract with a faked HTTP response instead
    (`GoogleCustomSearchArtworkProviderTests`). **This should be treated as unverified until the
    user adds a real key and it is tried against a real table** - flagged explicitly rather than
    quietly treated the same as everything else that was actually checked.
  - Query built as `"<title> <manufacturer> pinball machine"` (manufacturer included when known, the
    same disambiguating role it plays in `VpsDbMatcher`). Not tuned against real search results for
    the same reason above - untested judgement, not a measured choice.

### Choosing between sources, per table

The maintainer asked for "an option to change between scrapers, or use one for some tables and
another for others." Built as `CompositeArtworkProvider`, the one thing actually registered as
`IArtworkProvider` - `VpsDbArtworkProvider` and `GoogleCustomSearchArtworkProvider` are both
registered under their own concrete types instead, resolved only by the composite:

- A table with an entry in `NudgeSettings.TableArtworkSourceOverrides` (keyed by file path) asks
  **only** that named source - an explicit per-table choice is honoured, never silently
  second-guessed by falling back to something else if it finds nothing.
- A table with no override tries `NudgeSettings.DefaultArtworkSourceName` first, then every other
  registered source in turn - one source finding nothing never stops another from filling the table
  in, which is the "best for filling in the images" goal the maintainer stated directly.
- The cache (`IArtworkCache`) is keyed by **(source name, table path)**, not table path alone -
  otherwise switching a table from one source to another would keep silently serving the first
  source's stale cached image back out, since the cache would have no way to know a different
  source was asked for.
- No UI exists yet for either setting - both are plain `NudgeSettings` fields a future settings
  screen can read/write; see docs/IMPLEMENTATION-STATUS.md.

### Browsing and hand-picking a specific image

A follow-up request: "so without actually having the images on the device the user can go into the
three lines and click on... maybe swap between the google search... and the vpx db scraper... and
hand select a good image". `CompositeArtworkProvider`/`IArtworkProvider` only ever return the one
automatic choice, so a separate, smaller interface was added rather than overloading that one:
`IArtworkBrowser` (`Nudge.Media.ArtworkBrowser`), with two operations:

- `SearchAsync(table, sourceName)` - lightweight `ArtworkCandidate` references (a URL and a
  description) from one *named* source, nothing downloaded or cached yet. `VpsDbArtworkProvider`
  returns every image (table screenshot **and** backglass) from every entry `VpsDbMatcher.FindAllMatches`
  finds - deliberately every plausible option, not the one `FindMatch`/`GetArtworkAsync` would have
  disambiguated down to automatically. `GoogleCustomSearchArtworkProvider` returns up to 8 image
  search results instead of just the first, for the same one search-quota cost either way.
- `SelectAsync(table, candidate)` - downloads, resizes, and permanently caches the one candidate the
  user picked, under its own source's name, so it comes back out of `IArtworkProvider.GetArtworkAsync`
  from then on exactly as if it had been found automatically.

Both concrete providers implement a small internal `IArtworkCandidateSource` interface for this
(search + resolve); `ArtworkBrowser` is a thin dispatcher over whichever one the caller names -
it never searches or resolves anything itself. No UI exists yet - see
docs/IMPLEMENTATION-STATUS.md for what a picker screen would need.

## Controller support: default keybindings and the Windows APIs used to fake them

Requested by the maintainer as "trick the computer into thinking the trigger is Right Shift" - a
controller plugged in for table play should work with an unmodified Visual Pinball install, the
same role tools like JoyToKey/AntiMicroX play for other games. This is not a ToS or scraping
question like the artwork sources above - it is entirely local, entirely within what the user's own
machine and their own peripherals are allowed to do to each other, so there was no legal/ethical
gate to work through here, only a technical one.

### Visual Pinball's default keybindings

VPX ships with default keyboard bindings, user-remappable inside VPX itself (Keys tab of its own
options). Cited from the community's documented default keymap (VPForums community reference
thread - the same kind of unofficial-but-authoritative community source vps-db itself is):

| Action | Default key |
| --- | --- |
| Left flipper | Left Shift |
| Right flipper | Right Shift |
| Plunger | Enter |
| Start game | 1 |
| Insert coin | 5 |
| Nudge forward | Space |
| Nudge left | Z |
| Nudge right | / |
| Left magnasave | Left Ctrl |
| Right magnasave | Right Ctrl |
| Tilt | T |
| Menu | Esc |

`Nudge.Core.Models.ControllerMapping.Default` mirrors this table so a controller works out of the
box against a Visual Pinball install nobody has customized; `NudgeSettings.ControllerButtonOverrides`
exists for the minority of users who *have* remapped VPX itself.

### Two Windows APIs, and one real gotcha in each

**Reading a pad**: XInput (`xinput1_4.dll`, Windows 8+), falling back to `xinput9_1_0.dll` (present
since Vista) if the modern one can't be loaded - so this works on any supported Windows version
without the caller needing to know which is present. `XInputGetState`'s `wButtons` bitmask covers
the face/shoulder/D-pad/thumbstick-click buttons directly; the analog triggers (0-255) and
thumbsticks (-32767..32767) need an explicit dead zone before counting as "pressed" or they'd read
resting noise as input - Microsoft's own published XInput dead-zone constants were used (30/255 for
triggers, 7849/32767 for thumbsticks) rather than inventing new ones.

**Faking a key press**: Win32 `SendInput`, using hardware **scan codes**
(`KEYEVENTF_SCANCODE` + `MapVirtualKey(vk, MAPVK_VK_TO_VSC)`) rather than virtual-key codes. This
matters specifically for a game like Visual Pinball: an application reading the keyboard through
DirectInput sees scan codes, not the higher-level virtual-key events a plain `keybd_event`-style VK
press produces, so a naive VK-based synthesizer can be invisible to exactly the kind of application
this feature targets. This is the same technique AutoHotkey and JoyToKey use for game input.

**The gotcha**: `SendInput`'s marshaled `INPUT` struct is a union of `MOUSEINPUT`/`KEYBDINPUT`/
`HARDWAREINPUT`. On x64, `MOUSEINPUT` is the largest of the three at 32 bytes, making the real
`sizeof(INPUT)` 40 bytes - but a naive C# translation that only declares the `KEYBDINPUT` branch of
the union (since that's the only one ever used) makes `Marshal.SizeOf<INPUT>()` report a smaller,
wrong size. `SendInput`'s own `cbSize` parameter is supposed to equal Windows' real `sizeof(INPUT)`,
and when it doesn't, **Windows silently rejects the entire call - returns 0, sends nothing, no
exception, no visible error of any kind.** This is a well-known trap when hand-rolling this P/Invoke
(the same one many `SendInput`-on-x64 Stack Overflow questions turn out to be). Fixed by explicitly
sizing the union struct to 32 bytes (`[StructLayout(LayoutKind.Explicit, Size = 32)]`) even though
only the keyboard branch is ever populated. Verified directly, not just reasoned through from the
docs: an independent re-implementation of the same call in a throwaway harness confirmed
`Marshal.SizeOf<INPUT>()` reports 40 bytes and `SendInput` itself returns 1 (accepted) rather than 0
(silently rejected) once the fix was in place.

### What wasn't verified

No physical controller was plugged in at verification time, and no real VPX instance was launched
with controller support enabled - `XInputControllerReader` was confirmed to report "not connected"
correctly for all four controller slots (no exception, no crash), and `SendInput` was confirmed to
be *accepted* by Windows, but a real end-to-end "controller press moves a real flipper in a real
running table" pass has not happened. Flagged rather than assumed, the same way the Google Custom
Search artwork source is flagged pending real credentials.

## Seamless launch: what "the table is ready" can actually mean from outside VPX

Requested by the maintainer: click a table, see a loading screen in Nudge, and land directly in the
table once it's showing - not watch Visual Pinball's own window boot up. VPX has no IPC or API that
announces "I have finished loading and am now showing the table" - nothing like that is documented,
and nothing in the vpin/vpxtool source (already consulted for the BIFF format work above) exposes
one either. The only externally observable signal is the process's own window: whether it has been
created, is visible, and has a real (not placeholder) size. That is a weaker signal than "finished
loading" - a table with a slow-loading script could still show a moment of its own incomplete render
after Nudge cuts over - but it is the practical ceiling of what's achievable without VPX's
cooperation, and a large improvement over showing nothing at all during the wait.

Built as `Nudge.Vpx.Windowing.TableWindowWatcher`: poll `EnumWindows`/`GetWindowRect` for a window
belonging to the launched process of at least 200×200 (filtering out any tiny placeholder/helper
window), require it to stay like that across polls spanning ~450ms before trusting it (debounced,
so a fleeting window during startup is never mistaken for the real one), then attempt
`SetForegroundWindow` on it.

### A real finding from live verification: SetForegroundWindow's own success/failure is a red herring

The first version reported "ready" based on whether `SetForegroundWindow` itself succeeded. Verified
live (launching a real Notepad process as a stand-in, since spinning up a real VPX instance for every
verification pass isn't practical) rather than trusting unit tests against fakes alone - and the real
run immediately surfaced something the fakes couldn't: `SetForegroundWindow` was declined by Windows
every single time in the verification environment (a console process with no foreground focus of its
own), even though the target window had been correctly detected and was genuinely stable and visible.

This traces to Windows' own documented anti-focus-stealing rules: a process may only successfully
call `SetForegroundWindow` under specific conditions (it currently owns the foreground itself, it
received the most recent input event, etc.) that have nothing to do with whether the *target* window
is actually ready to be shown. Two things follow from this:

1. **The activation attempt's own success or failure must not gate the "ready" signal.** Detecting a
   stable, real-sized window is the meaningful fact; whether Windows also grants the foreground steal
   is a separate, best-effort concern layered on top. Fixed by decoupling the two:
   `ActivateWhenReadyAsync` now returns `true` from detection alone, still attempts the foreground
   steal as a side effect (logged at Debug if declined), and never lets a declined steal suppress the
   result the caller actually needs.
2. **In the real production scenario this is likely a non-issue anyway.** VPX's window is a window
   belonging to a process Nudge itself just launched, at a moment Nudge (the thing the user just
   clicked) most likely already owns the foreground - one of the documented conditions under which a
   newly created window is allowed to take focus on its own, without any explicit
   `SetForegroundWindow` call succeeding from the launcher at all. The explicit attempt exists as a
   best-effort fallback for the case where that natural handoff doesn't happen (e.g. the user
   alt-tabbed away from Nudge during the wait), not as the primary mechanism.

Re-verified after the fix: the same real Notepad launch correctly reported `true`, and a process that
exits immediately with no window (a `cmd.exe /c exit`) correctly reported `false` rather than waiting
out the full internal timeout.

### What wasn't verified

No real VPX instance was launched to confirm this against the real target application - Notepad was
used as a stand-in window-creating process (see above), which exercises the exact same Win32 APIs
this watches, but confirming Nudge's own window is what naturally has focus at launch time (the
premise behind point 2 above) needs Nudge's own UI, which does not call any of this yet.

## Duplicate table detection: why hashing is fine here but not in the routine scan

Pulled forward from Phase 8 after the maintainer reported a table appearing twice in Nudge that they
believed they had already deleted. Investigated against the maintainer's real, live database and
Tables folder (see the scanner section above) rather than assumed: both files were confirmed still
genuinely present on disk (identical size, identical original modification date), so this was never
a stale-data bug - it's a real, literal duplicate file the scanner is correctly reporting twice.

The routine scanner deliberately never hashes file contents (documented above, under Phase 3's known
limitations) because paying that cost on every rescan of a large library would violate AGENTS.md's
performance budget. But confirming two files are *truly* identical - not just coincidentally the same
size - genuinely requires reading their full contents; there is no reliable shortcut. The resolution
is not to relax the routine scan's fingerprint, but to make duplicate-finding a separate, on-demand
operation the user explicitly triggers, where paying that cost is reasonable specifically because it
only happens when asked for.

The cost is kept proportional by a cheap pre-filter: group the library by the file size already on
record (free - already in the database from the routine scan), and only hash a file that shares its
size with at least one other file. On the maintainer's real 61-table installation this meant hashing
2 files, not 61, to find the one real duplicate pair - full verification (below) took about 6 seconds,
dominated by actually reading the two ~410MB files, not by any per-file overhead across the library.

## Where sources conflicted

None yet. If a future Visual Pinball release changes something documented here, record the
conflict and which version it applies to rather than overwriting this file silently.
