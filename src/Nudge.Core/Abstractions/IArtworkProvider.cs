using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Finds artwork for a table. Two implementations exist in <c>Nudge.Media</c>:
/// <c>VpsDb.VpsDbArtworkProvider</c> (the community vps-db dataset) and
/// <c>GoogleImages.GoogleCustomSearchArtworkProvider</c> (Google's official, sanctioned Custom
/// Search API - never a direct scrape of Google, which its Terms of Service prohibit). A third,
/// <c>CompositeArtworkProvider</c>, is the one actually registered as <see cref="IArtworkProvider"/>:
/// it picks which of the others to try for a given table (per-table override, else a default order
/// with automatic fallback) - see docs/RESEARCH-NOTES.md and <c>NudgeSettings.DefaultArtworkSourceName</c>
/// / <c>TableArtworkSourceOverrides</c>.
/// </summary>
public interface IArtworkProvider
{
    /// <summary>A short, stable, human-readable name for this specific source, e.g. "vps-db" or
    /// "Google Images" - used to refer to it in settings (<c>NudgeSettings.DefaultArtworkSourceName</c>,
    /// <c>TableArtworkSourceOverrides</c>) and in a future UI picker. Never shown as a raw enum or id.</summary>
    string Name { get; }

    /// <summary>
    /// Never throws for "nothing found" - an unmatched table, a network error, a disabled setting,
    /// or missing artwork in an otherwise-matched entry are all the same ordinary, expected
    /// <see cref="Result{T}.Failure"/> outcome. The caller falls back to its own placeholder tile;
    /// nothing here retries or blocks waiting for a slow network.
    /// </summary>
    Task<Result<ArtworkImage>> GetArtworkAsync(VpxTableFile table, CancellationToken cancellationToken = default);
}
