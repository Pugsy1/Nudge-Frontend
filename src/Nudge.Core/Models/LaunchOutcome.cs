namespace Nudge.Core.Models;

/// <summary>
/// What happened when Nudge launched a table and waited for Visual Pinball to exit again - the
/// "VPX launches -> VPX exits -> back to the library" core loop from AGENTS.md section 1.
/// </summary>
public sealed record LaunchOutcome
{
    /// <summary>Full path of the executable that was actually launched.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// The process's exit code. A non-zero value is not itself treated as a launch failure here -
    /// Nudge started Visual Pinball and it ran and exited, which is a completed launch regardless of
    /// what happened during play. Interpreting a crash is the health system's job, in a later phase.
    /// </summary>
    public required int ExitCode { get; init; }

    /// <summary>How long Visual Pinball was running for, from launch to exit.</summary>
    public required TimeSpan Duration { get; init; }
}
