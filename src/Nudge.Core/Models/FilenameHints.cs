namespace Nudge.Core.Models;

/// <summary>
/// What can be inferred from a table's filename alone, following the loose "Title (Manufacturer
/// Year).vpx" convention parts of the community use - inconsistently. Never assumed to be present:
/// checked against real table filenames during Phase 2 development, and roughly half followed no
/// parseable convention at all ("Batman66.vpx", "Cheech&amp;Chong.vpx"). A filename that doesn't
/// match produces <see cref="Empty"/>, not a wrong guess.
/// </summary>
public sealed record FilenameHints
{
    public string? Title { get; init; }

    public string? Manufacturer { get; init; }

    public int? Year { get; init; }

    /// <summary>
    /// Trailing tags such as "MOD", "VR ROOM", a mod author's name, or a version number - kept as
    /// free text rather than parsed further, since there is no shared convention for what these
    /// mean or how they're formatted.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    public static FilenameHints Empty { get; } = new();

    public bool HasManufacturerYear => Manufacturer is not null && Year is not null;

    public bool IsEmpty => Title is null && Manufacturer is null && Year is null && Tags.Count == 0;
}
