namespace Nudge.Core.Models;

/// <summary>What happened during one library scan.</summary>
public sealed record ScanResult
{
    public required int TotalFilesFound { get; init; }

    /// <summary>Freshly read - new since the last scan, or changed (different size or last-write time).</summary>
    public required int Scanned { get; init; }

    /// <summary>Unchanged since the last scan - fingerprint matched, so the file was not re-read.</summary>
    public required int Skipped { get; init; }

    /// <summary>Found on disk but could not be read as a table file.</summary>
    public required int Failed { get; init; }

    /// <summary>Previously scanned but no longer found on disk, and removed from the database.</summary>
    public required int Removed { get; init; }

    public required TimeSpan Duration { get; init; }

    public IReadOnlyList<string> FailedPaths { get; init; } = [];
}

/// <summary>Progress reported partway through a scan, e.g. for a progress bar.</summary>
public sealed record ScanProgress(int Completed, int Total, string CurrentFileName);
