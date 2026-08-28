using CommunityToolkit.Mvvm.ComponentModel;
using Nudge.Core.Models;

namespace Nudge.App.ViewModels;

/// <summary>
/// One tile in the library grid. Wraps a scanned <see cref="VpxTableFile"/> with the small amount
/// of extra display logic the grid needs.
/// </summary>
public sealed partial class TableTileViewModel : ObservableObject
{
    public TableTileViewModel(VpxTableFile table)
    {
        Table = table;
    }

    public VpxTableFile Table { get; }

    public string DisplayTitle => Table.DisplayTitle;

    /// <summary>Null when the table's year is unknown - used by the year sort to group those last.</summary>
    public int? Year => Table.DisplayYear;

    public string Subtitle
    {
        get
        {
            if (Table.DisplayManufacturer is not null && Table.DisplayYear is not null)
            {
                return $"{Table.DisplayManufacturer} • {Table.DisplayYear}";
            }

            return Table.DisplayManufacturer ?? Table.DisplayYear?.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// Whether this table is starred. An observable property (not a plain field/getter, unlike the
    /// rest of this class) because the grid needs to re-sort live when it changes and the star
    /// button's own fill needs to update immediately on click - see
    /// LibraryViewModel.TablesView.IsLiveSorting.
    /// </summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>
    /// How confident Nudge is that it identified this table correctly. Surfaced as a small status
    /// lamp on the tile, behind a settings toggle - the underlying data has always been computed
    /// (see AGENTS.md section 7, "confidence is a first-class concept"), it just wasn't shown.
    /// </summary>
    public Confidence Confidence => Table.Confidence;

    public string ConfidenceLabel => Table.Confidence switch
    {
        Core.Models.Confidence.High => "Identified confidently",
        Core.Models.Confidence.Medium => "Probably identified correctly",
        Core.Models.Confidence.Low => "Poorly identified - check this table's details",
        _ => "Not identified"
    };

    /// <summary>The reasoning behind the identification, shown as the tile's tooltip when lamps are on.</summary>
    public string EvidenceSummary => Table.Evidence.Summary;

    /// <summary>
    /// Placeholder art shown in place of real artwork - Nudge has no artwork pipeline yet (that's a
    /// later phase, per AGENTS.md's phase table). Just the title's first letter, so a scanned
    /// library reads as a grid of distinct tiles rather than a bare list, without pretending to be
    /// real box art.
    /// </summary>
    public string Initial => string.IsNullOrWhiteSpace(DisplayTitle)
        ? "?"
        : char.ToUpperInvariant(DisplayTitle.Trim()[0]).ToString();
}
