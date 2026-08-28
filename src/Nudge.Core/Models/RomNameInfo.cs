namespace Nudge.Core.Models;

/// <summary>
/// What Nudge could determine about a table's PinMAME ROM name by searching its VBScript source -
/// see docs/RESEARCH-NOTES.md. Unlike <see cref="VpxTableFile"/>, producing this requires reading
/// the much larger <c>GameStg</c> storage, so it is a deliberately separate, second-pass operation
/// rather than part of the fast library scan (AGENTS.md section 4.5).
/// </summary>
public sealed record RomNameInfo
{
    /// <summary>Null when no ROM name could be determined - an ordinary outcome, not a failure.</summary>
    public string? RomName { get; init; }

    public required Confidence Confidence { get; init; }

    public required DetectionEvidence Evidence { get; init; }

    public static RomNameInfo NotFound(DetectionEvidence evidence) => new()
    {
        RomName = null,
        Confidence = Confidence.Unknown,
        Evidence = evidence
    };
}
