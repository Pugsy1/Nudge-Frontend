namespace Nudge.Core.Models;

/// <summary>
/// A piece of artwork Nudge found for a table, already decoded and resized to a sensible display
/// size (AGENTS.md's performance budget: "decode images at target size, never full size") - the
/// caller never needs to know whether this came from disk cache or a fresh network fetch, or
/// re-decode anything itself.
/// </summary>
public sealed record ArtworkImage
{
    /// <summary>Encoded image bytes (PNG), already sized for display - not the original full-resolution source.</summary>
    public required byte[] Data { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Where this came from, in words a user could understand, e.g. "Backglass (vps-db)".</summary>
    public required string Source { get; init; }
}
