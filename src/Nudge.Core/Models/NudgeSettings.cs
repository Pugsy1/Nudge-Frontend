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
