# Nudge

An open-source Windows frontend, game library, and launcher for
[Visual Pinball X](https://github.com/vpinball/vpinball). Think Steam or Playnite, built
specifically for VPX — it finds your table collection, presents it as browsable artwork, and
launches tables in Desktop or VR, with full controller support from the library all the way into
the table.

## What it does

**Finding your installation.** Nudge locates Visual Pinball from the registry, the conventional
install paths across every fixed drive, and `VPinballX.ini` — or you point it at a folder. It tells
you exactly what it found: which rendering build, which architecture, what version, with the
reasoning behind every answer shown rather than guessed.

**Your library.** Your tables folder is scanned into a local database and shown as a grid, a
carousel, or a list, with search, sorting, favourites, and an adjustable tile density. Scanning is
incremental — unchanged files are never re-read — and Nudge watches the folder while it's running,
so adding or removing a table is picked up on its own without a restart or a manual rescan.

**Artwork.** Table artwork is fetched from the community
[vps-db](https://github.com/VirtualPinballSpreadsheet/vps-db) dataset, with Google's official
Custom Search API available as an optional second source if you supply your own API key. You can
pin an individual table to a specific source, or browse every image a source can find for a table
and hand-pick the one you want. This is entirely opt-in: leave it off and Nudge never touches the
network at all.

**Playing.** Launch in Desktop or VR. An Xbox-style controller drives the library itself — D-pad or
left stick to move, A to play, X for details, Y to customize, left bumper to favourite, B to go
back, Start for settings — and keeps working inside the table, where Nudge translates the pad into
the keystrokes Visual Pinball expects. The default mapping matches VPX's own stock keybindings, so
an untouched install works out of the box; every button can be rebound if yours isn't.

**Per-table detail.** Notes and how-to-play text, custom artwork, title, author and release date
overrides, a hover video, and a check for whether the table's PinMAME ROM is actually present.

**41 themes**, light and dark, including a true-black OLED option, with custom window chrome and a
borderless fullscreen mode.

## Not built yet

Being straight about the gaps:

- **Library-wide health checking.** ROM availability is checked per table, on demand; there is no
  "show me everything that's broken" sweep.
- **Import.** No drag-and-drop, no archive extraction, no importing media from PinballX/PinballY/
  PinUP Popper.
- **Duplicate detection has no UI.** The engine exists and works — it finds byte-identical copies of
  the same table — but nothing in the app calls it yet.
- **Backup/export and auto-update.**

[`docs/IMPLEMENTATION-STATUS.md`](docs/IMPLEMENTATION-STATUS.md) has the full breakdown of what's
built, what isn't, and — importantly — what has been verified against real hardware and data versus
what has only been reasoned about.

## Building it

Requires the .NET 10 SDK and Windows (WPF is Windows-only).

```bash
git clone <this repo>
cd nudge
dotnet build Nudge.slnx
dotnet test tests\Nudge.Vpx.Tests\Nudge.Vpx.Tests.csproj
dotnet run --project src\Nudge.App\Nudge.App.csproj
```

There are four test projects (`Nudge.Vpx.Tests`, `Nudge.Data.Tests`, `Nudge.Library.Tests`,
`Nudge.Media.Tests`) totalling 299 tests. If you're setting this up for the first time and don't
have the SDK yet, see [`build/install-dotnet-sdk.ps1`](build/install-dotnet-sdk.ps1) for an
installer script that keeps it off the C: drive.

## Project layout

```
src/
  Nudge.Core     Domain models, service interfaces. No file I/O, no UI, no database.
  Nudge.Vpx      VPX discovery, executable identification, table files, launching, controller input.
  Nudge.Data     SQLite persistence via EF Core.
  Nudge.Library  Folder scanning, folder watching, duplicate detection.
  Nudge.Media    Artwork sources (vps-db, Google Custom Search), caching, resizing.
  Nudge.App      WPF UI — MVVM, theming, the library, settings.
tests/
  Nudge.Vpx.Tests | Nudge.Data.Tests | Nudge.Library.Tests | Nudge.Media.Tests
  Nudge.TestSupport  Synthetic installation layouts, fakes, a hand-built PE image generator.
docs/
  RESEARCH-NOTES.md         What we know about how Visual Pinball X actually works.
  IMPLEMENTATION-STATUS.md  What's built, what isn't, what's verified vs. not.
  TESTING.md                Manual test steps.
```

## Safety

Nudge only ever reads your Visual Pinball installation — it never modifies, moves, or deletes
anything inside it. The one exception, arriving in a later phase and always behind an explicit
confirmation with an automatic backup, is a per-table settings override file that VPX itself
already supports. Full detail in `AGENTS.md` section 6.

Network access is opt-in and off by default. With it off, Nudge makes no outbound requests of any
kind.

## Contributing

This project follows the phase plan and locked decisions documented in `AGENTS.md`. Read it before
opening a PR — it explains *why* things are built the way they are, not just what to build next.

## License

MIT — see [`LICENSE`](LICENSE).
