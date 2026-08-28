using Nudge.Core.Models;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Scans a folder of <c>.vpx</c> tables and writes what it finds to the database. Unlike
/// <see cref="ITableFileReader"/>, which reads one file, this walks a whole folder, decides which
/// files actually need re-reading (see AGENTS.md's incremental-scanning requirement), and removes
/// database rows for files that have disappeared since the last scan.
/// </summary>
public interface IVpxLibraryScanner
{
    Task<ScanResult> ScanAsync(
        string installationId,
        string tablesPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
