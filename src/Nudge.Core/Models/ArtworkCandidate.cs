namespace Nudge.Core.Models;

/// <summary>
/// A reference to one possible piece of artwork for a table - not yet downloaded, resized, or
/// cached. Returned by <see cref="Abstractions.IArtworkBrowser"/> so a picker UI can show several
/// options and let the user choose, without Nudge fetching every candidate just to display a list.
/// </summary>
public sealed record ArtworkCandidate
{
    /// <summary>Direct URL to the full image. What <see cref="Abstractions.IArtworkBrowser.SelectAsync"/> downloads if this candidate is chosen.</summary>
    public required string ImageUrl { get; init; }

    /// <summary>Which named source this came from (e.g. "vps-db", "Google Images") - the same name <c>IArtworkProvider.Name</c> reports.</summary>
    public required string SourceName { get; init; }

    /// <summary>A short label for display, e.g. "Medieval Madness - table image".</summary>
    public required string Description { get; init; }
}
