namespace Nudge.Core.Models;

/// <summary>
/// Everything Nudge remembers between runs in Phase 1. Persisted as JSON.
/// </summary>
public sealed class NudgeSettings
{
    /// <summary>Bumped when the shape of this file changes, so old files can be migrated.</summary>
    public int SettingsVersion { get; set; } = 1;

    /// <summary>Id of the installation the user confirmed. Null until they confirm one.</summary>
    public string? SelectedInstallationId { get; set; }

    /// <summary>
    /// Root path of the confirmed installation. Stored alongside the id so a settings file remains
    /// readable by a human and recoverable if ids ever change.
    /// </summary>
    public string? SelectedInstallationPath { get; set; }

    /// <summary>"Dark" or "Light".</summary>
    public string ThemeName { get; set; } = "Dark";

    /// <summary>
    /// The interface's overall material - shape and depth - independent of <see cref="ThemeName"/>,
    /// which only decides colour. Stored as the name of the UI's style enum ("Pin", "Crisp") so an
    /// unrecognised value from an older or newer build falls back to the default rather than
    /// failing. Any style combines with any theme.
    /// </summary>
    public string UiStyleName { get; set; } = "Pin";

    /// <summary>
    /// An explicit folder to scan for <c>.vpx</c> tables, overriding the one detected from the
    /// selected installation. Null or blank means "use the detected folder", which is the normal
    /// case; this exists for setups where the tables genuinely live somewhere else (a second drive,
    /// a shared library, a VPinballX.ini path Nudge's own detection does not follow).
    /// </summary>
    public string? TablesPathOverride { get; set; }

    /// <summary>
    /// How the library grid is ordered. Stored as the name of the UI's sort-order enum, so an
    /// unrecognised value (an older or newer build) falls back to the default rather than failing.
    /// </summary>
    public string SortOrder { get; set; } = "TitleAscending";

    /// <summary>
    /// Shows the per-table identification-confidence lamp in the library. Off by default: the
    /// confidence data is always computed (AGENTS.md section 7), but it's diagnostic detail rather
    /// than something most users need on screen all the time.
    /// </summary>
    public bool ShowConfidence { get; set; }

    /// <summary>
    /// Whether the library's 2D/VR switch was last left on VR. Only honoured when the confirmed
    /// installation actually has a VR-capable build.
    /// </summary>
    public bool PreferVr { get; set; }

    /// <summary>
    /// Full paths of tables marked as favourites, across all installations. Deliberately stored
    /// here rather than as a database column: it is a pure UI preference (like ThemeName), and
    /// piggybacking on the settings file already read/written on every launch avoids needing a
    /// Nudge.Data migration for what is, functionally, a starred-item list.
    /// </summary>
    public List<string> FavoriteTablePaths { get; set; } = [];

    /// <summary>
    /// How many tiles the library grid fits per row, 3-8. Pure UI preference like ThemeName, not
    /// something the scan or database needs to know about.
    /// </summary>
    public int TablesPerRow { get; set; } = 8;

    /// <summary>Installations the user has confirmed or added manually, newest last.</summary>
    public List<KnownInstallation> KnownInstallations { get; set; } = [];

    /// <summary>
    /// Whether the neumorphic theme draws its soft drop shadows at all. On by default; a pure
    /// display preference like ThemeName, not something a scan or the database needs to know about.
    /// </summary>
    public bool EnableShadows { get; set; } = true;

    /// <summary>
    /// Scales every shadow's opacity, as a percentage (25-175, 100 = the theme's authored default).
    /// Deliberately does not touch blur radius or shadow depth - those were sized to fit the space
    /// each control actually has to render a shadow into without it hard-clipping (see the note in
    /// Nudge.App's Themes/Effects.xaml), and scaling them here would risk reintroducing that.
    /// </summary>
    public int ShadowIntensityPercent { get; set; } = 100;

    /// <summary>
    /// How the library screen arranges tables - stored as the name of the UI's layout-mode enum
    /// (see <c>Nudge.App.ViewModels.LibraryLayoutMode</c>), the same pattern <see cref="SortOrder"/>
    /// already uses, so an unrecognised value falls back to the default instead of failing.
    /// </summary>
    public string LayoutMode { get; set; } = "Grid";

    /// <summary>
    /// Opt-in, off by default: whether Nudge may fetch table artwork from the internet (the
    /// community vps-db dataset - see <c>IArtworkProvider</c> and docs/RESEARCH-NOTES.md) when a
    /// table has none locally. Off by default because it is the only thing Nudge does that talks to
    /// the network at all; a user who never enables it gets a purely offline app.
    /// </summary>
    public bool FetchArtworkFromInternet { get; set; }

    /// <summary>
    /// API key for the optional Google Custom Search artwork source (a second
    /// <c>IArtworkProvider</c>, alongside vps-db - see docs/RESEARCH-NOTES.md). Null/empty means
    /// that source is not configured and is skipped entirely - Nudge never scrapes Google directly
    /// (against its Terms of Service); this is the official, sanctioned API, and it is the user's
    /// own key, obtained from their own Google Cloud project.
    /// </summary>
    public string? GoogleCustomSearchApiKey { get; set; }

    /// <summary>
    /// The Programmable Search Engine ID ("cx") paired with <see cref="GoogleCustomSearchApiKey"/>.
    /// Both must be set for the Google Images source to be usable.
    /// </summary>
    public string? GoogleCustomSearchEngineId { get; set; }

    /// <summary>
    /// Which artwork source (by its <c>IArtworkProvider.Name</c>, e.g. "vps-db" or "Google Images")
    /// to try first for a table with no entry in <see cref="TableArtworkSourceOverrides"/>. The
    /// composite provider still falls through to every other configured source afterward if the
    /// first one finds nothing - this only decides the order, not an exclusive choice.
    /// </summary>
    public string DefaultArtworkSourceName { get; set; } = "vps-db";

    /// <summary>
    /// Pins specific tables to a specific artwork source by name, keyed by the table's file path -
    /// "use one scraper for some tables and another for other tables", per the maintainer's request.
    /// A table with no entry here uses <see cref="DefaultArtworkSourceName"/> and the automatic
    /// fallback order instead. Only tables the user has explicitly overridden appear here.
    /// </summary>
    public Dictionary<string, string> TableArtworkSourceOverrides { get; set; } = [];

    /// <summary>
    /// User-authored notes (a description, how-to-play text) and an optional custom artwork
    /// override for one table, keyed by its file path - written from the library's per-table
    /// customization page. Purely user content: Nudge never generates or scrapes this itself, and
    /// most tables have no entry here at all.
    /// </summary>
    public Dictionary<string, TableCustomization> TableCustomizations { get; set; } = [];

    /// <summary>
    /// Tints each library tile's title/subtitle band with the active theme's accent colour at low
    /// opacity, instead of leaving it the same flat surface as the rest of the card. Purely a display
    /// preference; the tint always follows whatever theme is selected rather than being a colour of
    /// its own.
    /// </summary>
    public bool TintTableBanner { get; set; }

    /// <summary>
    /// Plays a table's video in place of its artwork while the pointer is over its tile, in the grid
    /// and carousel layouts. On by default now that Nudge finds videos on its own
    /// (<see cref="Abstractions.ITableVideoLocator"/>) rather than only playing ones the user hand-
    /// assigned via <see cref="TableCustomization.VideoPath"/> - previously the honest default was
    /// off, because for almost every library there was nothing to play. Tables with no video found
    /// simply keep showing their artwork, so leaving this on costs nothing.
    /// </summary>
    public bool EnableMediaTrailers { get; set; } = true;

    /// <summary>
    /// Plays those hover videos silently. On by default - a wall of tiles that each start making
    /// noise as the pointer crosses them is the wrong default, so sound is strictly opt-in.
    /// </summary>
    public bool MuteMediaTrailers { get; set; } = true;

    /// <summary>
    /// Per-button remaps layered over <c>ControllerMapping.Default</c> (which already mirrors
    /// Visual Pinball's own out-of-the-box keybindings), keyed by <c>ControllerButton</c> name with
    /// a <c>VirtualKey</c> name as the value. Empty means every button uses the default mapping -
    /// only needed if the user has also remapped keys inside VPX itself.
    /// </summary>
    public Dictionary<string, string> ControllerButtonOverrides { get; set; } = [];

    /// <summary>
    /// API key for the optional YouTube trailer lookup (<c>ITrailerProvider</c> - see
    /// docs/RESEARCH-NOTES.md). Null/empty means that feature is not configured and is skipped
    /// entirely - Nudge never downloads or scrapes video, only looks up a video id via YouTube's own
    /// official Data API v3 for the UI to embed, using the user's own key from their own Google
    /// Cloud project.
    /// </summary>
    public string? YouTubeApiKey { get; set; }
}

/// <summary>One table's user-authored customization - see <see cref="NudgeSettings.TableCustomizations"/>.</summary>
public sealed class TableCustomization
{
    public string Description { get; set; } = string.Empty;

    public string HowToPlay { get; set; } = string.Empty;

    /// <summary>Full path to a local image file the user chose to stand in for whatever IArtworkProvider would otherwise find - null means no override, fall back to the normal provider lookup.</summary>
    public string? CustomImagePath { get; set; }

    /// <summary>
    /// Full path to a local video file (a trailer or gameplay capture) to play on hover when
    /// <see cref="NudgeSettings.EnableMediaTrailers"/> is on. Null means this table has no video and
    /// simply keeps showing its artwork.
    ///
    /// <para>A local file always wins over <see cref="TrailerYouTubeId"/>: it is the user's own
    /// choice, it is usually real gameplay rather than an overview video, and it plays as a true WPF
    /// element instead of through the embedded browser.</para>
    /// </summary>
    public string? VideoPath { get; set; }

    /// <summary>
    /// A YouTube video id the user picked from the trailer search, played through YouTube's own
    /// embedded player on hover. Null means nothing has been chosen and Nudge falls back to whatever
    /// <c>ITableTrailerProvider</c> matches automatically.
    ///
    /// <para>An id rather than a file because these are never downloaded - doing so would breach
    /// YouTube's Terms of Service, and no source publishes pinball video Nudge could legitimately
    /// fetch and cache.</para>
    /// </summary>
    public string? TrailerYouTubeId { get; set; }

    /// <summary>User-supplied title override. Null/blank means keep showing the table's own DisplayTitle.</summary>
    public string? CustomTitle { get; set; }

    /// <summary>User-supplied "made by" credit override, shown alongside the year in a tile's subtitle.</summary>
    public string? CustomAuthor { get; set; }

    /// <summary>User-supplied release-date override, as free text (the source .vpx's own year metadata is often missing or wrong).</summary>
    public string? CustomDate { get; set; }

    /// <summary>
    /// Hides this table from the library entirely (grid, carousel, list, search - everywhere) without
    /// touching the scanned .vpx file or the database row it produced. Settings has its own list of
    /// every currently-hidden table so one can always be found and un-hidden again later.
    /// </summary>
    public bool IsHidden { get; set; }
}

/// <summary>A remembered installation. Deliberately minimal: paths get re-validated on every start.</summary>
public sealed class KnownInstallation
{
    public string Id { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset DateAdded { get; set; }

    public bool IsDefault { get; set; }
}
