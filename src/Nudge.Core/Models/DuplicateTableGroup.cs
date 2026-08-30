namespace Nudge.Core.Models;

/// <summary>
/// Two or more tables in the same installation whose file contents are byte-for-byte identical -
/// the same table copied to more than one path (e.g. a plain and a VR-folder copy of the same
/// release), not merely a same-sized coincidence. See <c>IDuplicateTableFinder</c>.
/// </summary>
public sealed record DuplicateTableGroup
{
    public required IReadOnlyList<VpxTableFile> Tables { get; init; }
}

/// <summary>Progress through hashing the (usually small) subset of tables that share a file size with at least one other table.</summary>
public sealed record DuplicateScanProgress(int Completed, int Total, string CurrentFileName);
