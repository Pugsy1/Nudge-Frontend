using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Media;

/// <summary>
/// The browsing counterpart to <see cref="Core.Abstractions.IArtworkProvider"/>: "give me several
/// candidates" instead of "give me the one best image", plus a way to commit to whichever one was
/// picked. Implemented by the same concrete classes that implement <c>IArtworkProvider</c>
/// (<c>VpsDbArtworkProvider</c>, <c>GoogleCustomSearchArtworkProvider</c>) - <see cref="ArtworkBrowser"/>
/// is the thing that actually implements <c>Core.Abstractions.IArtworkBrowser</c>, dispatching to
/// whichever of these the caller names.
/// </summary>
internal interface IArtworkCandidateSource
{
    string Name { get; }

    Task<Result<IReadOnlyList<ArtworkCandidate>>> SearchCandidatesAsync(VpxTableFile table, CancellationToken cancellationToken);

    Task<Result<ArtworkImage>> ResolveCandidateAsync(VpxTableFile table, ArtworkCandidate candidate, CancellationToken cancellationToken);
}
