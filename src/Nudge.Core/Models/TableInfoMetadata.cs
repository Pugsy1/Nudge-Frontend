namespace Nudge.Core.Models;

/// <summary>
/// The text fields Visual Pinball stores in a table's <c>TableInfo</c> OLE storage. Every value is
/// exactly what the table author typed - or didn't - the last time the table was saved in the
/// editor.
///
/// **Frequently blank or wrong.** Most tables in circulation are mods of mods, and this metadata
/// is very often inherited from an earlier version rather than updated by whoever last touched the
/// file. Verified directly against real tables during Phase 2 development: one real file's
/// <see cref="TableName"/> read "Strange Science" while its filename was "Breaking Badv2.vpx" -
/// the mod chain had moved on and the metadata hadn't. See docs/RESEARCH-NOTES.md.
/// </summary>
public sealed record TableInfoMetadata
{
    public string? TableName { get; init; }

    public string? AuthorName { get; init; }

    public string? AuthorEmail { get; init; }

    public string? AuthorWebSite { get; init; }

    /// <summary>
    /// Free text exactly as stored - VPX imposes no date format at all. Real-world examples seen
    /// during Phase 2 development: "01/04/22", "09.07.2025", "7/24/2021", "june 2018", "2-4-2022",
    /// "December 2019", "July 17, 2021". Deliberately never parsed into a <see cref="DateTime"/>:
    /// the format is not consistent enough to guess at without risking a wrong answer presented as
    /// a confident one.
    /// </summary>
    public string? ReleaseDate { get; init; }

    public string? TableVersion { get; init; }

    public string? TableBlurb { get; init; }

    public string? TableDescription { get; init; }

    public string? TableRules { get; init; }

    public static TableInfoMetadata Empty { get; } = new();

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(TableName)
        && string.IsNullOrWhiteSpace(AuthorName)
        && string.IsNullOrWhiteSpace(TableVersion)
        && string.IsNullOrWhiteSpace(ReleaseDate);
}
