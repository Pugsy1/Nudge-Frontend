using Nudge.Core.Abstractions;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Media;

/// <inheritdoc cref="IArtworkBrowser" />
public sealed class ArtworkBrowser : IArtworkBrowser
{
    private readonly IReadOnlyDictionary<string, IArtworkCandidateSource> _sourcesByName;

    internal ArtworkBrowser(IEnumerable<IArtworkCandidateSource> sources)
    {
        _sourcesByName = sources.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> AvailableSourceNames => [.. _sourcesByName.Keys];

    public Task<Result<IReadOnlyList<ArtworkCandidate>>> SearchAsync(
        VpxTableFile table,
        string sourceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        return _sourcesByName.TryGetValue(sourceName, out IArtworkCandidateSource? source)
            ? source.SearchCandidatesAsync(table, cancellationToken)
            : Task.FromResult(Result<IReadOnlyList<ArtworkCandidate>>.Failure($"Unknown artwork source \"{sourceName}\"."));
    }

    public Task<Result<ArtworkImage>> SelectAsync(
        VpxTableFile table,
        ArtworkCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(candidate);

        return _sourcesByName.TryGetValue(candidate.SourceName, out IArtworkCandidateSource? source)
            ? source.ResolveCandidateAsync(table, candidate, cancellationToken)
            : Task.FromResult(Result<ArtworkImage>.Failure($"Unknown artwork source \"{candidate.SourceName}\"."));
    }
}
