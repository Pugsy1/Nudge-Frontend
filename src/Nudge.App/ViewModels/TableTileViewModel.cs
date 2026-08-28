using Nudge.Core.Models;

namespace Nudge.App.ViewModels;

/// <summary>
/// One tile in the library grid. Wraps a scanned <see cref="VpxTableFile"/> with the small amount
/// of extra display logic the grid needs.
/// </summary>
public sealed class TableTileViewModel
{
    public TableTileViewModel(VpxTableFile table)
    {
        Table = table;
    }

    public VpxTableFile Table { get; }

    public string DisplayTitle => Table.DisplayTitle;

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
    /// Placeholder art shown in place of real artwork - Nudge has no artwork pipeline yet (that's a
    /// later phase, per AGENTS.md's phase table). Just the title's first letter, so a scanned
    /// library reads as a grid of distinct tiles rather than a bare list, without pretending to be
    /// real box art.
    /// </summary>
    public string Initial => string.IsNullOrWhiteSpace(DisplayTitle)
        ? "?"
        : char.ToUpperInvariant(DisplayTitle.Trim()[0]).ToString();
}
