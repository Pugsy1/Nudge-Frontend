namespace Nudge.Core.Models;

/// <summary>Whether a PinMAME ROM Nudge extracted from a table's script actually exists on disk.</summary>
public enum RomAvailabilityStatus
{
    /// <summary>VPinMAME's ROM folder could not be determined - not every machine has it registered.</summary>
    Unknown = 0,

    Found,

    Missing
}

/// <summary>
/// The result of checking one ROM name against VPinMAME's configured ROM folder - a building block
/// toward the health system (AGENTS.md section 1 calls this "the actual differentiator").
/// </summary>
public sealed record RomAvailability
{
    public required string RomName { get; init; }

    public required RomAvailabilityStatus Status { get; init; }

    /// <summary>The full path Nudge checked for. Null when <see cref="Status"/> is Unknown.</summary>
    public string? CheckedPath { get; init; }
}
