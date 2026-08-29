using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Finds artwork for a table. Implemented by <c>Nudge.Media.VpsDb.VpsDbArtworkProvider</c>, which
/// looks the table up in the community vps-db dataset and fetches its image over the network - see
/// docs/RESEARCH-NOTES.md for why a local-only source (embedded table images, a media folder
/// convention) was not used instead.
/// </summary>
public interface IArtworkProvider
{
    /// <summary>
    /// Never throws for "nothing found" - an unmatched table, a network error, a disabled setting,
    /// or missing artwork in an otherwise-matched entry are all the same ordinary, expected
    /// <see cref="Result{T}.Failure"/> outcome. The caller falls back to its own placeholder tile;
    /// nothing here retries or blocks waiting for a slow network.
    /// </summary>
    Task<Result<ArtworkImage>> GetArtworkAsync(VpxTableFile table, CancellationToken cancellationToken = default);
}
