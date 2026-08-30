using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Media.VpsDb;

/// <inheritdoc cref="IArtworkProvider" />
/// <remarks>
/// Order of operations: check the permanent per-table cache first (an already-resolved table never
/// touches the network again); if the setting is off or nothing is cached, look the table up in the
/// vps-db index; if matched and it has an image, rate-limit, download, resize, cache, and return it.
/// Any failure along the way - no match, no image on the match, a network error, a disabled setting
/// - is the same ordinary "nothing found" outcome; nothing here retries or blocks the caller.
/// </remarks>
public sealed class VpsDbArtworkProvider : IArtworkProvider, IArtworkCandidateSource
{
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(300);

    private readonly IVpsDbIndex _index;
    private readonly IArtworkCache _cache;
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<VpsDbArtworkProvider> _logger;

    private readonly RateLimiter _rateLimiter = new(MinimumRequestInterval);

    public VpsDbArtworkProvider(
        IVpsDbIndex index,
        IArtworkCache cache,
        HttpClient httpClient,
        ISettingsService settingsService,
        IPathRedactor redactor,
        ILogger<VpsDbArtworkProvider> logger)
    {
        _index = index;
        _cache = cache;
        _httpClient = httpClient;
        _settingsService = settingsService;
        _redactor = redactor;
        _logger = logger;
    }

    public string Name => "vps-db";

    public async Task<Result<ArtworkImage>> GetArtworkAsync(VpxTableFile table, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        ArtworkImage? cached = await _cache.TryGetAsync(Name, table.Path, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return Result<ArtworkImage>.Success(cached);
        }

        NudgeSettings settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.FetchArtworkFromInternet)
        {
            return Result<ArtworkImage>.Failure("Fetching artwork from the internet is turned off.");
        }

        IReadOnlyList<VpsDbEntry> entries = await _index.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        VpsDbEntry? match = VpsDbMatcher.FindMatch(table, entries);
        if (match is null)
        {
            return Result<ArtworkImage>.Failure("No matching table found in vps-db.");
        }

        string? imageUrl = VpsDbMatcher.BestImageUrl(match, out string sourceDescription);
        if (imageUrl is null)
        {
            return Result<ArtworkImage>.Failure("A matching table was found, but it has no image in vps-db.");
        }

        Result<ArtworkImage> fetched = await FetchAndCacheAsync(table.Path, imageUrl, sourceDescription, cancellationToken)
            .ConfigureAwait(false);
        return fetched;
    }

    /// <summary>
    /// Every matched entry's table screenshots and backglasses, as unfetched candidates - for
    /// browsing (<see cref="Core.Abstractions.IArtworkBrowser"/>), unlike <see cref="GetArtworkAsync"/>'s
    /// single automatic choice. Still gated by the same internet-fetch setting: browsing is still
    /// fetching (the vps-db index itself, if not already cached), just not committing to an image yet.
    /// </summary>
    public async Task<Result<IReadOnlyList<ArtworkCandidate>>> SearchCandidatesAsync(
        VpxTableFile table,
        CancellationToken cancellationToken)
    {
        NudgeSettings settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.FetchArtworkFromInternet)
        {
            return Result<IReadOnlyList<ArtworkCandidate>>.Failure("Fetching artwork from the internet is turned off.");
        }

        IReadOnlyList<VpsDbEntry> entries = await _index.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        List<VpsDbEntry> matches = VpsDbMatcher.FindAllMatches(table, entries);

        List<ArtworkCandidate> candidates = matches
            .SelectMany(VpsDbMatcher.AllImageUrls)
            .Select(pair => new ArtworkCandidate { ImageUrl = pair.Url, SourceName = Name, Description = pair.Description })
            .ToList();

        return candidates.Count == 0
            ? Result<IReadOnlyList<ArtworkCandidate>>.Failure("No candidate images found in vps-db.")
            : Result<IReadOnlyList<ArtworkCandidate>>.Success(candidates);
    }

    public Task<Result<ArtworkImage>> ResolveCandidateAsync(
        VpxTableFile table,
        ArtworkCandidate candidate,
        CancellationToken cancellationToken) =>
        FetchAndCacheAsync(table.Path, candidate.ImageUrl, candidate.Description, cancellationToken);

    private async Task<Result<ArtworkImage>> FetchAndCacheAsync(
        string tablePath,
        string imageUrl,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        try
        {
            await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            byte[] sourceBytes = await _httpClient.GetByteArrayAsync(imageUrl, timeout.Token).ConfigureAwait(false);
            byte[] resized = ImageResizer.ResizeToPng(sourceBytes, out int width, out int height);

            var image = new ArtworkImage
            {
                Data = resized,
                Width = width,
                Height = height,
                Source = sourceDescription
            };

            await _cache.SaveAsync(Name, tablePath, image, cancellationToken).ConfigureAwait(false);
            return Result<ArtworkImage>.Success(image);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                        or SixLabors.ImageSharp.ImageFormatException)
        {
            // ImageFormatException (and its subclass UnknownImageFormatException) covers ImageSharp's
            // own decode failures - a corrupt or unrecognised image from the network is exactly as
            // ordinary an outcome here as a network error, not a reason to throw out of an artwork
            // lookup. Confirmed by test: this does NOT derive from InvalidOperationException, despite
            // looking like it should.
            _logger.LogDebug(ex, "Could not fetch or decode artwork from {Url}.", _redactor.Redact(imageUrl));
            return Result<ArtworkImage>.Failure("Could not fetch artwork.");
        }
    }

}
