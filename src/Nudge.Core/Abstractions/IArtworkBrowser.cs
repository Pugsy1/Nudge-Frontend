using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Lets a user browse and hand-pick artwork for one table, rather than only ever getting whatever
/// <see cref="IArtworkProvider"/>'s automatic choice was - the maintainer's "so without actually
/// having the images on the device the user can go into the three lines and click on... maybe swap
/// between the google search... and the vpx db scraper... and hand select a good image" request.
/// Implemented by <c>Nudge.Media.ArtworkBrowser</c>. No UI uses this yet - see
/// docs/IMPLEMENTATION-STATUS.md for what a picker screen would need.
/// </summary>
public interface IArtworkBrowser
{
    /// <summary>Every source this browser can search, by name - e.g. "vps-db", "Google Images" - for a picker to offer as a dropdown/tab choice.</summary>
    IReadOnlyList<string> AvailableSourceNames { get; }

    /// <summary>
    /// Lightweight candidates for a table from one specific named source - nothing is downloaded,
    /// resized, or cached yet. An unknown source name, or a source with nothing configured (e.g.
    /// Google Images with no API key set), returns a <see cref="Result{T}.Failure"/> the same as
    /// "no candidates found" - never an exception for what is, from the caller's side, an ordinary
    /// "nothing to show" outcome.
    /// </summary>
    Task<Result<IReadOnlyList<ArtworkCandidate>>> SearchAsync(
        VpxTableFile table,
        string sourceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads, resizes, and permanently caches one candidate the user picked, under its own
    /// source name - after this, <see cref="IArtworkProvider.GetArtworkAsync"/> for that table and
    /// source returns this hand-picked image from cache, the same as if it had been found
    /// automatically, until the user picks something else.
    /// </summary>
    Task<Result<ArtworkImage>> SelectAsync(
        VpxTableFile table,
        ArtworkCandidate candidate,
        CancellationToken cancellationToken = default);
}
