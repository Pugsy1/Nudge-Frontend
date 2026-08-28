using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Reads a single <c>.vpx</c> table file: its OLE <c>TableInfo</c> metadata plus what the filename
/// suggests, combined into one <see cref="VpxTableFile"/> with confidence and evidence attached.
///
/// This reads a single file. Walking a folder of many tables, grouping duplicates, and writing
/// results to a database is <c>Nudge.Library</c>'s job, arriving in a later phase - this interface
/// deliberately does not know about folders.
/// </summary>
public interface ITableFileReader
{
    /// <summary>
    /// Reads one table file. Fails when the path does not exist or is not a readable OLE compound
    /// document - a corrupt or non-VPX file is an expected, ordinary failure, not a bug.
    /// </summary>
    Task<Result<VpxTableFile>> ReadAsync(string path, CancellationToken cancellationToken = default);
}
