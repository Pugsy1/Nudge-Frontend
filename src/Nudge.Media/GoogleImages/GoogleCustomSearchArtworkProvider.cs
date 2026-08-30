using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Media.GoogleImages;

/// <summary>
/// A second, optional artwork source using Google's own official Custom Search JSON API - never a
/// direct scrape of Google Search/Images, which Google's Terms of Service explicitly prohibit and
/// actively enforce with bot detection. This is the sanctioned alternative: a real API, with the
/// user's own API key and Programmable Search Engine ID, subject to Google's published quota
/// (100 free queries/day at the time this was written) rather than anything Nudge tries to work
/// around.
/// </summary>
/// <remarks>
/// Disabled (reports "not configured", the same ordinary not-found outcome as no match) until the
/// user supplies both <see cref="NudgeSettings.GoogleCustomSearchApiKey"/> and
/// <see cref="NudgeSettings.GoogleCustomSearchEngineId"/> - obtained from their own Google Cloud
/// project and https://programmablesearchengine.google.com/, never something Nudge can provision on
/// their behalf. See docs/IMPLEMENTATION-STATUS.md for the exact setup steps.
///
/// Built and unit-tested against Google's documented API contract with a faked HTTP response - not
/// verified against a real live call, since doing so needs credentials Nudge's own development
/// environment does not have. Flagged explicitly rather than claimed as verified; see
/// docs/RESEARCH-NOTES.md.
/// </remarks>
public sealed class GoogleCustomSearchArtworkProvider : IArtworkProvider
{
    private const string Endpoint = "https://customsearch.googleapis.com/customsearch/v1";
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(300);

    private readonly IArtworkCache _cache;
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<GoogleCustomSearchArtworkProvider> _logger;

    private readonly RateLimiter _rateLimiter = new(MinimumRequestInterval);

    public GoogleCustomSearchArtworkProvider(
        IArtworkCache cache,
        HttpClient httpClient,
        ISettingsService settingsService,
        IPathRedactor redactor,
        ILogger<GoogleCustomSearchArtworkProvider> logger)
    {
        _cache = cache;
        _httpClient = httpClient;
        _settingsService = settingsService;
        _redactor = redactor;
        _logger = logger;
    }

    public string Name => "Google Images";

    public async Task<Result<ArtworkImage>> GetArtworkAsync(VpxTableFile table, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        ArtworkImage? cached = await _cache.TryGetAsync(Name, table.Path, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return Result<ArtworkImage>.Success(cached);
        }

        NudgeSettings settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(settings.GoogleCustomSearchApiKey) || string.IsNullOrWhiteSpace(settings.GoogleCustomSearchEngineId))
        {
            return Result<ArtworkImage>.Failure("Google Images is not configured (no API key / search engine id).");
        }

        string? imageUrl;
        try
        {
            imageUrl = await FindImageUrlAsync(table, settings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "Google Custom Search request failed for {Title}.", table.DisplayTitle);
            return Result<ArtworkImage>.Failure("Could not search Google Images.");
        }

        if (imageUrl is null)
        {
            return Result<ArtworkImage>.Failure("No image found via Google Images.");
        }

        return await FetchAndCacheAsync(table.Path, imageUrl, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> FindImageUrlAsync(VpxTableFile table, NudgeSettings settings, CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        string query = BuildQuery(table);
        string url = $"{Endpoint}?key={Uri.EscapeDataString(settings.GoogleCustomSearchApiKey!)}"
                     + $"&cx={Uri.EscapeDataString(settings.GoogleCustomSearchEngineId!)}"
                     + $"&q={Uri.EscapeDataString(query)}&searchType=image&num=1&safe=active";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        using HttpResponseMessage response = await _httpClient.GetAsync(url, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogDebug(
                "Google Custom Search returned {StatusCode} for {Title}.",
                response.StatusCode,
                table.DisplayTitle);
            return null;
        }

        Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            GoogleCustomSearchResponse? parsed = await JsonSerializer
                .DeserializeAsync<GoogleCustomSearchResponse>(stream, cancellationToken: timeout.Token)
                .ConfigureAwait(false);

            return parsed?.Items?.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Link))?.Link;
        }
    }

    /// <summary>
    /// Manufacturer is included when known, the same disambiguating role it plays in
    /// <c>VpsDbMatcher</c>'s candidate narrowing - "Medieval Madness Williams pinball machine" is a
    /// meaningfully more specific query than "Medieval Madness pinball machine" alone.
    /// </summary>
    private static string BuildQuery(VpxTableFile table)
    {
        List<string> parts = [table.DisplayTitle];
        if (!string.IsNullOrWhiteSpace(table.DisplayManufacturer))
        {
            parts.Add(table.DisplayManufacturer);
        }

        parts.Add("pinball machine");
        return string.Join(' ', parts);
    }

    private async Task<Result<ArtworkImage>> FetchAndCacheAsync(string tablePath, string imageUrl, CancellationToken cancellationToken)
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
                Source = Name
            };

            await _cache.SaveAsync(Name, tablePath, image, cancellationToken).ConfigureAwait(false);
            return Result<ArtworkImage>.Success(image);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                        or SixLabors.ImageSharp.ImageFormatException)
        {
            _logger.LogDebug(ex, "Could not fetch or decode Google Images artwork from {Url}.", _redactor.Redact(imageUrl));
            return Result<ArtworkImage>.Failure("Could not fetch artwork.");
        }
    }
}
