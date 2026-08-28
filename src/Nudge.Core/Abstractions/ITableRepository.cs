using Nudge.Core.Models;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Persists the tables Nudge has scanned. Implemented by <c>Nudge.Data</c> against SQLite; this
/// interface knows nothing about EF Core, connection strings, or SQL - <c>Nudge.Core</c> has no
/// database code, per the architecture rule in AGENTS.md section 5.
/// </summary>
public interface ITableRepository
{
    /// <summary>Inserts or updates a single table. For scanning many files at once, prefer
    /// <see cref="UpsertManyAsync"/> - AGENTS.md's performance budget calls for batched writes
    /// (one transaction per few hundred rows), not one per file.</summary>
    Task UpsertAsync(string installationId, TableScanEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates many tables in a single transaction.</summary>
    Task UpsertManyAsync(
        string installationId,
        IReadOnlyList<TableScanEntry> entries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The size and last-write time last recorded for every known table path in an installation,
    /// keyed by path. Used by the scanner to decide which files can be skipped without re-reading
    /// them - see AGENTS.md's incremental-scanning performance requirement.
    /// </summary>
    Task<IReadOnlyDictionary<string, ScannedFileFingerprint>> GetFingerprintsAsync(
        string installationId,
        CancellationToken cancellationToken = default);

    /// <summary>Every table currently stored for an installation.</summary>
    Task<IReadOnlyList<VpxTableFile>> GetAllAsync(string installationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes rows for paths that are no longer present on disk - the scanner calls this with the
    /// set of paths it actually found, and anything stored but not in that set is deleted.
    /// </summary>
    Task<int> DeleteMissingAsync(
        string installationId,
        IReadOnlySet<string> currentPaths,
        CancellationToken cancellationToken = default);
}

/// <summary>What was recorded about a file the last time it was scanned, for incremental comparison.</summary>
public readonly record struct ScannedFileFingerprint(long FileSizeBytes, DateTimeOffset FileLastWriteTimeUtc);

/// <summary>One freshly-scanned table, bundled with the file facts needed to fingerprint it for next time.</summary>
public sealed record TableScanEntry(VpxTableFile Table, long FileSizeBytes, DateTimeOffset FileLastWriteTimeUtc);
