using System.Collections;

namespace Nudge.App.ViewModels;

/// <summary>
/// How the library grid is ordered.
/// </summary>
/// <remarks>
/// Sorting by when Nudge first saw a table, or when it was last played, is deliberately absent:
/// neither fact is stored anywhere yet. <c>VpxTableFile</c> carries only what can be read back off
/// the table file itself, and the database row behind it records a size and last-write time for
/// incremental scanning - not a first-seen timestamp, a play count, or a last-played time. Adding
/// those needs a schema change in <c>Nudge.Data</c>, which is the backend session's lane. See the
/// note in <c>LibraryViewModel</c>. Favourites did not need that: it is a pure UI preference (a
/// starred-item list), so it lives in the settings file instead, the same as ThemeName.
/// </remarks>
public enum TableSortOrder
{
    TitleAscending,
    TitleDescending,
    YearNewest,
    YearOldest,
    FavoritesOnly
}

/// <summary>
/// Orders tiles by release year, keeping tables with no known year together at the end rather than
/// letting them sort as though they were year zero.
/// </summary>
public sealed class TableYearComparer : IComparer
{
    private readonly bool _newestFirst;

    public TableYearComparer(bool newestFirst) => _newestFirst = newestFirst;

    public int Compare(object? x, object? y)
    {
        if (x is not TableTileViewModel left || y is not TableTileViewModel right)
        {
            return 0;
        }

        // Unknown years always sink to the bottom, whichever direction the known years run in.
        if (left.Year is null && right.Year is null)
        {
            return CompareTitles(left, right);
        }

        if (left.Year is null)
        {
            return 1;
        }

        if (right.Year is null)
        {
            return -1;
        }

        int byYear = _newestFirst
            ? right.Year.Value.CompareTo(left.Year.Value)
            : left.Year.Value.CompareTo(right.Year.Value);

        // Same year: fall back to title, so the order is stable and readable rather than arbitrary.
        return byYear != 0 ? byYear : CompareTitles(left, right);
    }

    private static int CompareTitles(TableTileViewModel left, TableTileViewModel right) =>
        string.Compare(left.DisplayTitle, right.DisplayTitle, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Orders tiles by title, in either direction.</summary>
public sealed class TableTitleComparer : IComparer
{
    private readonly bool _ascending;

    public TableTitleComparer(bool ascending) => _ascending = ascending;

    public int Compare(object? x, object? y)
    {
        if (x is not TableTileViewModel left || y is not TableTileViewModel right)
        {
            return 0;
        }

        int result = string.Compare(left.DisplayTitle, right.DisplayTitle, StringComparison.OrdinalIgnoreCase);
        return _ascending ? result : -result;
    }
}

/// <summary>
/// Orders by title within the favourited set. FilterTable already excludes everything unfavourited
/// when TableSortOrder.FavoritesOnly is selected, so the IsFavorite comparison here is only a
/// defensive tie-breaker, not the primary sort - kept in case an item's favourite state changes
/// mid-frame before the live filter has caught up.
/// </summary>
public sealed class TableFavoriteComparer : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (x is not TableTileViewModel left || y is not TableTileViewModel right)
        {
            return 0;
        }

        int byFavorite = right.IsFavorite.CompareTo(left.IsFavorite);
        return byFavorite != 0
            ? byFavorite
            : string.Compare(left.DisplayTitle, right.DisplayTitle, StringComparison.OrdinalIgnoreCase);
    }
}
