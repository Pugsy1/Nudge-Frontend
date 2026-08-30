using Nudge.Core.Models;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Finds tables within one installation that are exact, byte-for-byte duplicates of each other -
/// the same release copied to more than one path. Deliberately a separate, on-demand operation the
/// user explicitly triggers (like a "Find Duplicates" action), not something the routine
/// <see cref="IVpxLibraryScanner"/> pass does automatically: confirming two files are truly
/// identical requires hashing their full contents, which is exactly the cost the fast incremental
/// scan is deliberately designed to avoid paying on every rescan (see
/// docs/IMPLEMENTATION-STATUS.md's note on the scanner's size+last-write-time fingerprint).
/// </summary>
public interface IDuplicateTableFinder
{
    Task<IReadOnlyList<DuplicateTableGroup>> FindDuplicatesAsync(
        string installationId,
        IProgress<DuplicateScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
