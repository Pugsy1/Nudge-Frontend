using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;

namespace Nudge.Media.VpsDb;

/// <summary>
/// Reads the YouTube video id out of whatever vps-db entry <see cref="VpsDbMatcher"/> already
/// matches this table to for artwork.
///
/// Reusing that matcher rather than doing its own title comparison is the whole point: a plain
/// "does one name contain the other" test pairs "Batman66" and "BatmanDarkKnight" with the 1991
/// "Batman" entry, so two tables would silently play a video of a third, unrelated one. The shared
/// matcher was measured against a real collection and handles exactly those cases (camelCase
/// splitting, edition suffixes, and refusing to subset-match a short title). A wrong video is worse
/// than none, and identity has to be decided in one place.
/// </summary>
public sealed class VpsDbTrailerProvider : ITableTrailerProvider
{
    private readonly IVpsDbIndex _index;
    private readonly ILogger<VpsDbTrailerProvider> _logger;

    /// <summary>
    /// Answers are cached per table path, including "no video" (stored as an empty string, since
    /// the dictionary cannot hold a null value). Hovering the same tile repeatedly is normal, and
    /// re-running the match on every hover would re-scan all 2,570 entries each time.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public VpsDbTrailerProvider(IVpsDbIndex index, ILogger<VpsDbTrailerProvider> logger)
    {
        _index = index;
        _logger = logger;
    }

    public async Task<string?> GetYouTubeVideoIdAsync(VpxTableFile table, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(table.Path, out string? cached))
        {
            return string.IsNullOrEmpty(cached) ? null : cached;
        }

        try
        {
            IReadOnlyList<VpsDbEntry> entries = await _index.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
            VpsDbEntry? match = VpsDbMatcher.FindMatch(table, entries);

            string? videoId = match?.TutorialFiles
                .Select(t => t.YoutubeId)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

            _cache[table.Path] = videoId ?? string.Empty;
            return videoId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The index is fetched over the network; failing to reach it means no preview, which is
            // the same outcome as a table simply not having one. Not cached, so a later hover once
            // the network is back can still succeed.
            _logger.LogDebug(ex, "Could not look up a trailer for a table.");
            return null;
        }
    }

    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _index.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Pre-warming is an optimisation, not a requirement - a failure here just means the
            // first real lookup pays the cost instead, exactly as it did before.
            _logger.LogDebug(ex, "Could not pre-load the trailer index.");
        }
    }

    public async Task<IReadOnlyList<TrailerCandidate>> FindTrailersAsync(
        VpxTableFile table,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<VpsDbEntry> entries = await _index.GetEntriesAsync(cancellationToken).ConfigureAwait(false);

        // FindAllMatches, not FindMatch: a table often has several vps-db entries (different builds,
        // VR conversions, editions), and any of their videos is a legitimate choice for the picker
        // even though only one entry wins the automatic lookup.
        List<VpsDbEntry> matches = VpsDbMatcher.FindAllMatches(table, entries);

        return matches
            .SelectMany(entry => entry.TutorialFiles)
            .Where(file => !string.IsNullOrWhiteSpace(file.YoutubeId))
            .Select(file => new TrailerCandidate(
                file.YoutubeId!,
                string.IsNullOrWhiteSpace(file.Title) ? "Table video" : file.Title!))
            // The same video is frequently attached to more than one entry for the same table, and
            // offering the identical thumbnail twice makes the picker look broken.
            .DistinctBy(candidate => candidate.VideoId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
