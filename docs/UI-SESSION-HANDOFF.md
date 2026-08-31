# UI Session Handoff

Written 2026-08-29 by the outgoing UI session, for whichever session picks up
`Nudge.App` UI work next. Read this fully before touching XAML. It exists so you don't
have to re-derive things that were already expensive to work out once.

---

## 1. Read `AGENTS.md` first — this document does not replace it

`AGENTS.md` (repo root) is the authoritative project brief: what Nudge is, who the
maintainer is (a complete beginner — explain plainly, give exact steps, never assume they
can debug your output), the locked tech stack, VPX research findings, architecture layering,
safety rules, coding standards, performance budgets, and phase discipline. This document only
adds UI-specific context on top of it. If anything here ever conflicts with `AGENTS.md`,
`AGENTS.md` wins — flag the conflict to the maintainer rather than silently picking one.

**Your scope as the UI session**: `Nudge.App` only — Views, ViewModels, Themes, Assets,
Converters, window chrome, UI-facing services. You consume interfaces exposed by
`Nudge.Core`; you never implement backend logic. Scanning, health checks, ROM detection,
launch engine internals, database work, etc. belong to a separate backend session working in
`Nudge.Core`/`Nudge.Vpx`/`Nudge.Data`/`Nudge.Library`. If you find yourself about to write
`File.Exists`, `Process.Start`, or a database call inside `Nudge.App`, stop — that's a sign
the interface you need doesn't exist yet in `Nudge.Core`, and you should either ask the
maintainer to route it to the backend session or (if low-risk) define the interface shape
yourself in `Nudge.Core` and let backend fill it in, rather than reaching around it.

**Two related memory-system notes worth knowing about** (in
`C:\Users\Orion\.claude\projects\D--Nudge-Frontend\memory\`, not part of this repo):
- `vice-dl-scope-boundaries.md` describes a *different* session's role ("Vice DL") — it is
  not you; don't take on its scope.
- `standing-push-authorization.md` records that the maintainer gave standing authorization
  (2026-08-28) to `git push` to this repo (`Pugsy1/Nudge-Frontend`, remote `origin`) without
  asking each time. In practice, the maintainer has also been actively committing and
  pushing UI work themselves from a concurrent session throughout — don't be surprised if
  `git log` shows commits you didn't make, or if the working tree is unexpectedly clean.
  Always run `git status`/`git log` before assuming work is uncommitted.
- `phase1-standard-window-chrome.md` was stale (said custom chrome was deferred to Phase 4)
  and has been corrected in place with a supersession note — custom chrome is built and
  live. `docs/IMPLEMENTATION-STATUS.md` line 56 still has the old claim, see §6 below.

---

## 2. Architecture and practical build/run notes

- **Build with the explicit SDK path, not bare `dotnet`**: `D:\dotnet\dotnet.exe`. Bare
  `dotnet` on this machine may resolve to a different/older SDK on PATH.
- **Never touch the C: drive** for project work — everything lives on D:.
- Solution file is `Nudge.slnx` (the .NET 10 XML solution format).
- Theming is 100% resource-driven: every color, font, spacing, corner radius, border
  thickness, and effect used in a View comes from a `DynamicResource` defined in
  `Themes/*.xaml`. A hard-coded `Background="#1a1a2e"` in a View is a bug per `AGENTS.md`
  §7. This is not just a style preference — palette swapping works by replacing a whole
  `ResourceDictionary` at `Application.Resources.MergedDictionaries` top level, and
  `DynamicResource` only re-resolves against dictionaries actually in that merged set, so a
  hard-coded value simply won't repaint on theme change.
- Theme files: `Colors.Dark.xaml`, `Colors.Light.xaml`, `Colors.Jade.xaml`,
  `Colors.Sapphire.xaml`, `Colors.Crimson.xaml`, `Colors.Chrome.xaml`, `Colors.Hulk.xaml`,
  `Colors.Oled.xaml`, plus Light variants of each accent theme
  (`Colors.JadeLight.xaml`, `Colors.SapphireLight.xaml`, `Colors.CrimsonLight.xaml`,
  `Colors.ChromeLight.xaml`, `Colors.HulkLight.xaml`). `ThemeService.cs` maps the `AppTheme`
  enum to these filenames via `PaletteFileNames`. `LibraryViewModel.ThemeOptions` holds the
  display list shown in Settings.
- Layout constants (spacing, padding, corner radii, sizes) live in `Themes/Layout.xaml`,
  each with a comment explaining *why* the value is what it is where that's non-obvious —
  read those comments before changing a value, they usually cross-reference another part of
  the UI that depends on it (see the corner-button/header-clearance example below).
- Visual effects (drop shadows for the neumorphic look) live in `Themes/Effects.xaml`.

---

## 3. Design language: neumorphic "soft UI"

The maintainer is steering toward a soft, embossed look: large corner radii (bumped this
session — see §5), tight background/surface contrast, and — the key technique — a **true
two-shadow emboss** on tiles: one light `DropShadowEffect` and one dark `DropShadowEffect`,
each on its own separate, identically-shaped, fully-opaque `Border` layer stacked behind the
real content. This is necessary because **WPF only allows a single `Effect` per element** —
you cannot put two shadows on one Border, so the two-layer trick is the workaround, not a
stylistic choice. See the `TableTileTemplate` in `LibraryView.xaml` (`ShadowLayer` +
`HighlightLayer` Borders) for the reference implementation. Both layers use
`CacheMode="BitmapCache"` for performance — shadow effects are expensive to re-rasterize
every frame, and tiles animate on hover.

**A `Card.Neumorphic` style for this same treatment on Settings cards was started but never
finished** — see §6, Outstanding Work.

---

## 4. WPF gotchas discovered this session (expensive to rediscover — read before you hit them again)

These are root-caused, verified fixes. If you see a similar symptom, check here first.

1. **`ItemContainerGenerator` reentrancy crash** (`InvalidOperationException: Range of
   Remove(...) cannot include items without a corresponding UI element`, sometimes followed
   by a `NullReferenceException`) in `VirtualizingWrapPanel.cs`. Cause: calling
   `ItemContainerGenerator.Remove` *synchronously* from inside `OnItemsChanged`, which itself
   runs from inside the generator's own `CollectionChanged` handling — a reentrancy hazard.
   Fix: `OnItemsChanged` now only calls `InvalidateMeasure()`; actual cleanup happens later,
   from `MeasureOverride` → `RealizeItems`/`CleanupContainers`. Additionally, when
   `IndexFromGeneratorPosition` returns a negative index (the generator already dropped that
   mapping itself, e.g. when the collection empties entirely), skip
   `ItemContainerGenerator.Remove` and call only `RemoveInternalChildRange`. This surfaced via
   the Favourites filter (favourite → filter → unfavourite down to zero items).

2. **`AnimationException` on `IsHitTestVisible`** — WPF does not support animating that
   property via `ObjectAnimationUsingKeyFrames` (or any animation) at all. It threw silently
   on the render thread with no visible dialog, which froze the entire app on the splash
   screen forever. Don't animate `IsHitTestVisible`; animate `Visibility` (which already
   excludes an element from hit-testing when `Collapsed`) instead.

3. **`Thumb.Triggers` NameScope crash** (`InvalidOperationException: 'X' name cannot be
   found in the name scope of 'Thumb'`) — a plain `FrameworkElement.Triggers`
   (`EventTrigger`) cannot resolve `Storyboard.TargetName` for a name declared via a sibling
   property setter (e.g. `RenderTransform="{...}" x:Name="Foo"`) that lives outside the
   Thumb's own template NameScope. Fix: move both the named transform and the trigger/
   storyboard into the control's own `ControlTemplate`/`ControlTemplate.Triggers`. See
   `Slider.Standard` in `Controls.xaml`.

4. **`Setter.TargetName` cannot reach into a nested `Effect` object's sub-properties.** To
   change a shadow's color/blur/opacity on a trigger, you must swap the *entire* `Effect`
   resource (`Effect="{DynamicResource Effect.X.Hover}"`), not try to set a property inside
   an already-assigned `DropShadowEffect`.

5. **Fractional `BorderThickness` + a `RenderTransform` scale animation = visible sub-pixel
   shimmer/glitching.** Tile top/bottom borders were tried at `0.6` and glitched constantly
   during the hover scale animation. Use `0` or whole-pixel values only, never a fraction,
   on any element that also animates via `RenderTransform`.

6. **`CornerRadius` exceeding half a shape's shorter dimension auto-clamps to a full
   stadium/pill in WPF** — but two elements can both be individually "fully pill-clamped"
   and still look visually mismatched if their **aspect ratios** differ (a wider/shorter
   shape has more straight middle run relative to its curved ends than a more-square one).
   This bit the 2D/VR toggle: the track (112×30, ≈3.7:1) and the cap (52×22, ≈2.36:1) were
   both technically "pills" but read as mismatched. Fixed by narrowing the gap between their
   aspect ratios (track → 96×32 ≈3:1, cap → 44×24 ≈1.83:1), not by changing the radius value.

7. **`MC3024: Property 'Button.Style' is set more than once`** — happens if a Button has
   both a `Style="{StaticResource X}"` attribute *and* an inline `<Button.Style>` property
   element. If you need per-instance triggers on top of a shared base style, delete the
   `Style=` attribute and put `BasedOn="{StaticResource X}"` on the inline `<Style>` instead.

8. **Prefer UI Automation over raw cursor simulation for automated testing.** Raw
   `SetCursorPos`/`mouse_event`-style automation moves the **real system cursor**, which is a
   problem if the maintainer is using the machine at the same time (this was reported and
   initially misdiagnosed as a product bug — "teleporting mouse" — before being traced to my
   own test automation). Use UI Automation patterns (`InvokePattern`, `SelectionItemPattern`,
   `RangeValuePattern`, `ExpandCollapsePattern`) instead; they don't move the physical cursor.

9. **Verify single-instance before trusting a screenshot.** `PrintWindow`-based captures can
   return blank/stale images if more than one `Nudge.exe` process is running (e.g. from an
   interrupted edit/relaunch cycle) and you capture the wrong/backgrounded one. Check
   `Get-Process -Name Nudge` returns exactly one instance first; kill strays with
   `Stop-Process -Force` before relaunching and capturing.

---

## 5. What was done this session

**Crash/stability fixes** (all verified via UI Automation + log inspection):
- Favourites toggle/filter `ItemContainerGenerator` reentrancy crash (§4.1).
- Splash-screen freeze from animating `IsHitTestVisible` (§4.2).
- Slider-thumb hover crash from the `Thumb.Triggers` NameScope issue (§4.3).
- A "random popup" visual bug on navigating to Settings — misdiagnosed twice (as a focus
  ring, partially fixed with `Focusable="False"` + `Keyboard.ClearFocus()` in
  `SettingsView.xaml.cs`) before the real cause was found: a redundant `Popup` flyout still
  in `LibraryView.xaml` (a leftover settings flyout anchored to the gear button) racing with
  `ShellViewModel`'s full-page Settings navigation, both driven by the same `IsSettingsOpen`
  property. Fixed by deleting the redundant Popup entirely — the gear button now only
  navigates via `ToggleSettingsCommand`.
- Tile top/bottom border sub-pixel glitching during hover animation (§4.5).
- Mouse "teleporting" — root-caused as a testing-methodology artifact, not a product bug;
  fixed by changing how I test, not by changing app code (§4.8).

**Visual/UX work**:
- True two-shadow neumorphic emboss on library tiles (`ShadowLayer`/`HighlightLayer` in
  `TableTileTemplate`, `LibraryView.xaml`), with `BitmapCache` for performance. Hover/press
  states swap the whole `Effect` resource rather than mutating sub-properties (§4.4).
- Bumped corner radii for a softer look: `CornerRadius.S/M/L` 8/14/20 → 12/18/26,
  `CornerRadius.Card.Top` 14,14,0,0 → 18,18,0,0 (`Layout.xaml`).
- Tightened Surface/Background contrast and brightened bevel borders
  (`EdgeHighlight`/`RimLight`) across `Colors.Dark.xaml`, `Colors.Light.xaml`, and the Jade/
  Sapphire/Crimson/Chrome theme pairs; added a new `Color.Highlight` token to each, used by
  the two-shadow technique. **`Colors.Hulk.xaml`/`Colors.HulkLight.xaml` did NOT get this
  same tightening pass** (deliberately deferred — it has a distinct custom 60/30/10 scheme
  and needed separate consideration) — only got `Color.Highlight` added.
- Reduced blur/opacity on all original single-shadow effects (cards, buttons, tiles, inset,
  flyout) in `Effects.xaml` in response to a "too much bloom" complaint; also specifically
  removed irregular/mismatched glow (e.g. around the theme dropdown).
- Added a new **OLED true-black theme** (`Colors.Oled.xaml`) — flat solid-color black/white,
  not the radial-gradient background the other themes use, with Confidence/Status colors
  kept non-monochrome (matching the precedent set by Hulk).
- Added **5 new Light theme variants** (Jade/Sapphire/Crimson/Chrome/Hulk Light), each
  mirroring `Colors.Light.xaml`'s key set with per-theme accent tinting plus
  `Color.Highlight`. `ThemeService.AppTheme` enum and `LibraryViewModel.ThemeOptions` both
  updated to list them.
- **Grid density slider**: new `TablesPerRow` (`[ObservableProperty]`, default 8, persisted
  in `NudgeSettings`, clamped 3–8) drives a genuinely dynamic column count. Added a `Columns`
  `DependencyProperty` to `VirtualizingWrapPanel.cs` — when set (>0), the panel computes
  `_effectiveItemSize` from available width ÷ `Columns` each measure pass, and every internal
  usage (measure/arrange/realize/`IScrollInfo`) was updated to use that instead of the raw
  `ItemWidth`/`ItemHeight` properties. UI: a slider + grid icon in the Library header, bound
  `VirtualizingWrapPanel.Columns` to it via `RelativeSource AncestorType=ItemsControl`.
- Restyled `Slider.Standard` (`Controls.xaml`): recessed 8px track, fill bar computed via a
  new `SliderFillWidthConverter` (`IMultiValueConverter`, inputs: Value/Minimum/Maximum/
  track `ActualWidth`), Thumb grows smoothly on hover (fixed per §4.3).
- Flattened `Button.Secondary` and `ComboBox.Standard` borders (removed the bevel-gradient
  `EdgeHighlight` border brush and resting shadow effect, hover state now uses a flat
  `Brush.Control.BorderHover`) for visual consistency with the rest of the flattened-border
  pass.
- **Custom window chrome + true fullscreen launch**: `MainWindow.xaml` uses
  `WindowStyle="None"` with `WindowChrome`, hand-templated minimize/maximize/close buttons,
  and launches directly into borderless fullscreen via `SourceInitialized` →
  `ToggleFullscreen()`. This produces an always-visible floating `FullscreenCornerControls`
  overlay that must never overlap other top-of-window content — required two rounds of
  fixes: shrinking the corner buttons (46×38 → 34×28 → 30×22) and raising
  `Padding.Header`'s top clearance (16 → 26); `Layout.xaml` has an explicit comment
  cross-referencing these two numbers to prevent future regression — **if you ever resize
  the corner buttons or the header padding, check that comment and keep both in sync.**
- **Themed splash screen**: replaced the placeholder splash with the maintainer's actual
  wordmark asset (`Assets/Nudge Logo Words.png`, masked via a `Rectangle` with
  `Fill="{DynamicResource Brush.Accent}"` so it recolors with the active theme, matching how
  the Library page's top-left logo already worked), a pop-in scale animation, a themed
  loading bar, and a fade-out at the end. Time-boxed, does not block Setup/Library
  initializing underneath.
- **2D/VR toggle redesign** (`Segment.Track`/`SwitchCap` in `LibraryView.xaml`): fixed both
  the cap's material (background `Brush.Accent.Surface`, border removed) and — the final fix
  this session — the outer track's proportions, per §4.6. Track is now 96×32, cap 44×24
  (`TranslateTransform` slide target updated to 44 to match). The "2D"/"VR" labels now use
  per-instance inline `<Button.Style>` (`BasedOn="{StaticResource Button.Base}"`) with a
  `DataTrigger` on `IsVrMode` to color the active label with `Brush.Accent.Foreground` and
  the inactive one with `Brush.Text.Muted`.
- Shrunk the "LIBRARY" logo (127×72 → 99×56) and moved header spacing onto a dedicated
  `Padding.Header` resource, separate from `Padding.Page`, since the header bar reads as
  heavy/thick with full page padding.
- Scrollbar restyled thinner and shorter (`Size.Scrollbar` 6→4, `CornerRadius.Scrollbar`
  3→2) and moved down (`Margin.Scrollbar` top 16→36) to clear the corner controls.

---

## 6. Outstanding work — not started, or started and abandoned mid-way

1. ~~**`Card.Neumorphic` style is half-built and unused.**~~ **Resolved** by the following
   session (a full reskin pass toward cleaner, flatter neumorphic reference boards the
   maintainer supplied). Went with raised, matching those references: `Controls.xaml` now
   defines `Card.Neumorphic` as a `ContentControl` style using the same two-Border shadow/
   highlight-layer technique as `TableTileTemplate`, and `SettingsView.xaml`/`SetupView.xaml`
   use it for every card. `Surface.Inset` (recessed) is kept in `Controls.xaml` for anything
   genuinely recessed elsewhere, just no longer used for cards.

   **Same pass also touched shared theme infrastructure app-wide**, chasing the borderless,
   soft-shadow-only look in the reference boards rather than the previous diagonal-bevel-
   border version: `Layout.xaml` corner radii bumped again (S/M/L 12/18/26 → 16/24/32, Pill
   22 → 30, `Padding.Button`/`Padding.Card` increased for chunkier pills); `Effects.xaml`
   shadows softened (bigger blur, lower opacity, new `Effect.Button.Shadow.Hover`); and
   `Controls.xaml`'s `Button.Secondary`, `Segment.Track`, `Switch`, `TextBox.Search`, and
   `ComboBox.Standard` all dropped their visible borders in favour of an always-on resting
   shadow. `Brush.Surface.EdgeHighlight`/`RimLight` (the diagonal bevel gradients) are now
   only used by the library tile's own border - not retuned per-theme, since their visual
   weight is minor at that scale, but worth knowing if a future pass wants to go further.
   Verified: solution builds clean, and a real launch (screenshot) confirmed pill controls,
   rounded tile corners, and visible shadows are actually rendering, on the Hulk theme (only
   theme active at test time) - **not re-verified on every theme**, so keep an eye out if a
   specific palette looks off.

2. **Grid/list layout switcher was never built.** Only the density slider (§5, `TablesPerRow`
   3–8 columns) exists. A genuinely different layout mode (e.g. a compact list view) is a
   separate, unstarted feature.

3. **Table details page is blocked on a backend task.** A details view with a wordless icon
   button needs table metadata/artwork from a ScreenScraper API client that doesn't exist
   yet in `Nudge.Core`. I filed this for the backend session via `mcp__ccd_session__spawn_task`
   (title: "Add ScreenScraper API client to Nudge.Core", id `task_ec1ac0dc`) — as of this
   writing it had not been picked up. Check its status before starting the details page; if
   it's still not done, either ping the backend session or scope a details page that doesn't
   need scraped data yet.

4. **`Colors.Hulk.xaml`/`Colors.HulkLight.xaml` never got the Surface-tightening pass**
   applied to the other 6 dark/light theme pairs this session (§5) — they'll look visually
   inconsistent (less tight contrast, dimmer bevels) next to the others until someone does
   the same pass, adapted for Hulk's distinct 60/30/10 custom color scheme.

5. **Full regression test suite has not been re-run since this session's UI changes.**
   `docs/IMPLEMENTATION-STATUS.md` (as of Phase 2) records 111/111 passing, but that was
   before this session's changes and only covers non-UI test projects
   (`Nudge.Vpx.Tests`/`Nudge.Library.Tests`/`Nudge.Data.Tests` — there is no
   `Nudge.App.Tests` project; UI correctness is verified manually/via UI Automation, not
   unit tests). Run `D:\dotnet\dotnet.exe test` across the solution before shipping Phase 4
   as done, to make sure nothing outside the UI regressed.

6. **Two docs are now stale and should be corrected** (small fixes, safe to do yourself):
   - `docs/IMPLEMENTATION-STATUS.md` line 56 still says "custom window chrome is deferred to
     Phase 4" — it's built and live now (this session). Update that line and, if Phase 4's
     grid/UI work is otherwise complete enough, consider whether Phase 4's status line in
     `AGENTS.md` §9 should move from blank to a status note too (check with the maintainer
     before declaring a whole phase "Done" — that's their call per the phase-discipline rule).
   - `SettingsView.xaml`'s "Coming Later" card text still lists favourites and sorting as
     not-yet-implemented — both are implemented now (favourites this session; sorting was
     already in via `TableSortOrder.cs`/`LibraryViewModel`). Update or remove that card copy.

---

## 7. Process notes for whoever picks this up

- The maintainer has, at times, been actively testing and committing/pushing from a
  concurrent session while a UI session (me) was also working. Don't assume a clean working
  tree means nothing changed recently — check `git log` for commits you don't recognize
  before concluding your own edits are lost or duplicated.
- Per `AGENTS.md` §10: decide yourself on low-risk/reversible choices (naming, file layout,
  minor library picks); ask before anything architecturally major or hard to reverse; inspect
  the actual files rather than assuming; never claim something works without having verified
  it (build + manual/UI-Automation test, not just "it compiles in my head").
- Be careful with any automated UI interaction that could click a table tile — that triggers
  a real, unintended VPX launch if there's a valid table underneath it.
