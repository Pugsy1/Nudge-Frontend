namespace Nudge.App.ViewModels;

/// <summary>
/// Turns recorded play history into the short phrases the details page shows.
///
/// Its own class rather than private helpers on <see cref="TableDetailsViewModel"/> so the awkward
/// boundaries - nothing played, under a minute, exactly an hour, one versus many - can be checked
/// directly. Every one of those reads wrong if it is off by a little, and none of them is visible
/// until someone happens to have played a table for exactly that long.
/// </summary>
public static class PlayHistoryFormat
{
    /// <summary>
    /// Shown where there is nothing to report yet. An em dash rather than a hyphen: at the 28px size
    /// these numbers are set in, a hyphen reads as a stray mark or a minus sign, where a dash of this
    /// length is the conventional "no value" placeholder.
    /// </summary>
    public const string NoData = "—";

    /// <summary>
    /// Rounded to whole minutes. Nobody reading "how long have I played this" wants the seconds, and
    /// showing them invites reading the number as more precise than a wall-clock measurement of a
    /// session - which includes however long the table sat on its attract screen - can be.
    /// </summary>
    public static string Duration(long totalSeconds)
    {
        if (totalSeconds <= 0)
        {
            return NoData;
        }

        TimeSpan span = TimeSpan.FromSeconds(totalSeconds);

        if (span.TotalMinutes < 1)
        {
            return "Under a minute";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)span.TotalMinutes} min";
        }

        // "2 hr", not "2 hr 0 min" - a trailing zero unit reads as a measurement that happened to
        // land there rather than a round number.
        return span.Minutes == 0
            ? $"{(int)span.TotalHours} hr"
            : $"{(int)span.TotalHours} hr {span.Minutes} min";
    }

    public static string TimesPlayed(int count) => count switch
    {
        <= 0 => "Never",
        1 => "Once",
        2 => "Twice",
        _ => $"{count} times"
    };

    /// <summary>
    /// Relative for the recent past, an actual date once "N days ago" stops being something anyone
    /// can picture. Compared by calendar day, not by elapsed hours: something played at 11pm is
    /// "yesterday" at 1am, not "2 hours ago".
    /// </summary>
    public static string When(DateTimeOffset when, DateTime today)
    {
        int daysAgo = (today.Date - when.LocalDateTime.Date).Days;

        return daysAgo switch
        {
            <= 0 => $"Today, {when.LocalDateTime:HH:mm}",
            1 => $"Yesterday, {when.LocalDateTime:HH:mm}",
            < 7 => $"{daysAgo} days ago",
            < 14 => "Last week",
            _ => when.LocalDateTime.ToString("d MMMM yyyy")
        };
    }
}
