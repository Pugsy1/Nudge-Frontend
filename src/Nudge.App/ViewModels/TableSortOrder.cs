using System.Collections;
using Nudge.Core.Models;

namespace Nudge.App.ViewModels;

/// <summary>
/// How the library grid is ordered.
/// </summary>
/// <remarks>
/// Sorting by when Nudge first saw a table is still absent: that fact is stored nowhere.
/// <c>VpxTableFile</c> carries only what can be read back off the table file itself, and the
/// database row behind it records a size and last-write time for incremental scanning, not a
/// first-seen timestamp. Adding it needs a schema change in <c>Nudge.Data</c>.
///
/// Play count and last-played time no longer need one. They took the same route favourites did -
/// <c>NudgeSettings.TablePlayStats</c>, keyed by path, written as each session ends - because like a
/// starred-item list they are the user's own record rather than anything a scan produces.
/// </remarks>
public enum TableSortOrder
{
    TitleAscending,
    TitleDescending,
    YearNewest,
    YearOldest,
    FavoritesOnly,
    MostPlayed,
    RecentlyPlayed
}

/// <summary>
/// Orders by play history, with never-played tables always last - sorted by title among themselves
/// rather than left in whatever order the scan produced.
///
/// Takes a lookup rather than the stats themselves: the dictionary is updated in place as sessions
/// end, and a comparer holding a snapshot would silently sort by yesterday's numbers.
/// </summary>
public sealed class TablePlayComparer : IComparer
{
    private readonly Func<string, TablePlayStats?> _statsFor;
    private readonly bool _byRecency;

    public TablePlayComparer(Func<string, TablePlayStats?> statsFor, bool byRecency)
    {
        _statsFor = statsFor;
        _byRecency = byRecency;
    }

    public int Compare(object? x, object? y)
    {
        if (x is not TableTileViewModel left || y is not TableTileViewModel right)
        {
            return 0;
        }

        TablePlayStats? leftStats = _statsFor(left.Table.Path);
        TablePlayStats? rightStats = _statsFor(right.Table.Path);

        bool leftPlayed = leftStats is { TimesPlayed: > 0 };
        bool rightPlayed = rightStats is { TimesPlayed: > 0 };

        if (!leftPlayed && !rightPlayed)
        {
            return CompareTitles(left, right);
        }

        if (!leftPlayed)
        {
            return 1;
        }

        if (!rightPlayed)
        {
            return -1;
        }

        int result = _byRecency
            ? Nullable.Compare(rightStats!.LastPlayedAt, leftStats!.LastPlayedAt)
            : rightStats!.TimesPlayed.CompareTo(leftStats!.TimesPlayed);

        // Same count, or both never recorded a timestamp: the one with more hours on it goes first,
        // and failing that, title - so the order is stable rather than arbitrary.
        if (result == 0)
        {
            result = rightStats.TotalPlaySeconds.CompareTo(leftStats.TotalPlaySeconds);
        }

        return result != 0 ? result : CompareTitles(left, right);
    }

    private static int CompareTitles(TableTileViewModel left, TableTileViewModel right) =>
        string.Compare(left.DisplayTitle, right.DisplayTitle, StringComparison.OrdinalIgnoreCase);
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
