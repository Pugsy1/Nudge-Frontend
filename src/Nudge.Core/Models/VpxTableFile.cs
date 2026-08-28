namespace Nudge.Core.Models;

/// <summary>
/// Everything Nudge could determine about one <c>.vpx</c> table file: its OLE metadata, its
/// filename hints, and the confidence and evidence behind reconciling the two into a single
/// best-guess display title. <c>Nudge.Core</c> has no I/O; this is populated by
/// <c>Nudge.Vpx.TableFiles.VpxTableFileReader</c>.
/// </summary>
public sealed record VpxTableFile
{
    public required string Path { get; init; }

    public required string FileName { get; init; }

    public required long FileSizeBytes { get; init; }

    /// <summary>Raw text pulled from the file's <c>TableInfo</c> OLE storage. May be entirely empty.</summary>
    public required TableInfoMetadata TableInfo { get; init; }

    /// <summary>What the filename alone suggests. May be entirely empty.</summary>
    public required FilenameHints FilenameHints { get; init; }

    /// <summary>
    /// Nudge's single best-guess display title, reconciling <see cref="TableInfo"/> and
    /// <see cref="FilenameHints"/>. Never blank - falls back to the filename with its extension
    /// stripped when neither source offers anything usable.
    /// </summary>
    public required string DisplayTitle { get; init; }

    public string? DisplayManufacturer { get; init; }

    public int? DisplayYear { get; init; }

    public required Confidence Confidence { get; init; }

    public required DetectionEvidence Evidence { get; init; }
}
