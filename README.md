# Nudge

An open-source Windows frontend, game library, and launcher for
[Visual Pinball X](https://github.com/vpinball/vpinball). Think Steam or Playnite, built
specifically for VPX — finds your table collection, presents it as browsable artwork, launches
tables in Desktop or VR, and tells you which tables are actually broken and why.

Nudge is under active development. See the status section below for what's real right now.

## Status: Phase 1

Nudge is built in phases, each one fully working and verified before the next starts. Right now
Nudge can find a Visual Pinball installation on your machine — or let you point it at one — and
tell you exactly what it found: which rendering build, which architecture, what version, with the
reasoning behind every answer shown, never guessed.

**Nudge does not yet browse your tables, show artwork, or launch anything.** That's not a bug —
it's the current, deliberate scope. See [`docs/IMPLEMENTATION-STATUS.md`](docs/IMPLEMENTATION-STATUS.md)
for the full breakdown of what's built and what isn't yet, and `AGENTS.md` for the complete phase
plan.

## Building it

Requires the .NET 10 SDK and Windows (WPF is Windows-only).

```bash
git clone <this repo>
cd nudge
dotnet build Nudge.slnx
dotnet test tests\Nudge.Vpx.Tests\Nudge.Vpx.Tests.csproj
dotnet run --project src\Nudge.App\Nudge.App.csproj
```

If you're setting this up for the first time and don't have the SDK yet, see
[`build/install-dotnet-sdk.ps1`](build/install-dotnet-sdk.ps1) for an installer script that keeps
it off the C: drive.

## Project layout

```
src/
  Nudge.Core     Domain models, service interfaces. No file I/O, no UI, no database.
  Nudge.Vpx      VPX installation discovery, executable identification, settings.
  Nudge.App      WPF UI — MVVM, theming, the setup screen.
tests/
  Nudge.Vpx.Tests    Unit tests for discovery and identification.
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

## Contributing

This project follows the phase plan and locked decisions documented in `AGENTS.md`. Read it before
opening a PR — it explains *why* things are built the way they are, not just what to build next.

## License

MIT — see [`LICENSE`](LICENSE).
