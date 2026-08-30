using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Nudge.Media.GoogleImages;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nudge.Media.Tests;

/// <summary>
/// Built and tested against Google's documented Custom Search JSON API contract with a faked HTTP
/// response - not verified against a real live call, since that needs credentials Nudge's own
/// development environment does not have. See docs/RESEARCH-NOTES.md.
/// </summary>
public sealed class GoogleCustomSearchArtworkProviderTests
{
    private const string TablePath = @"D:\VPX\Tables\Medieval Madness.vpx";
    private const string ImageUrl = "https://example.test/medieval-madness.jpg";

    private readonly FakeArtworkCache _cache = new();

    [Fact]
    public async Task Fails_without_any_request_when_no_API_key_is_configured()
    {
        var settings = new FakeSettingsService(); // key and cx both left null
        var handler = new RoutingHandler(NeverCalled: true);

        Result<ArtworkImage> result = await CreateProvider(handler, settings).GetArtworkAsync(Table());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_without_any_request_when_the_key_is_set_but_the_search_engine_id_is_not()
    {
        var settings = new FakeSettingsService { ApiKey = "some-key", EngineId = null };
        var handler = new RoutingHandler(NeverCalled: true);

        Result<ArtworkImage> result = await CreateProvider(handler, settings).GetArtworkAsync(Table());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Finds_downloads_resizes_and_caches_the_first_image_result()
    {
        var settings = new FakeSettingsService { ApiKey = "test-key", EngineId = "test-cx" };
        byte[] sourceImage = BuildJpeg(1000, 1000);
        var handler = new RoutingHandler(
            searchResponseJson: """{ "items": [ { "link": "https://example.test/medieval-madness.jpg", "title": "Medieval Madness" } ] }""",
            imageBytes: sourceImage);

        Result<ArtworkImage> result = await CreateProvider(handler, settings).GetArtworkAsync(Table());

        result.IsSuccess.Should().BeTrue();
        result.Value.Width.Should().BeLessThanOrEqualTo(ImageResizer.MaxDimension);
        result.Value.Source.Should().Be("Google Images");
        _cache.Stored.Should().ContainKey(TablePath);
        handler.SearchRequestCount.Should().Be(1);
    }

    [Fact]
    public async Task Returns_the_cached_image_without_making_any_request()
    {
        var cachedImage = new ArtworkImage { Data = [1, 2, 3], Width = 5, Height = 5, Source = "Google Images" };
        _cache.Stored[TablePath] = cachedImage;
        var settings = new FakeSettingsService { ApiKey = "test-key", EngineId = "test-cx" };
        var handler = new RoutingHandler(NeverCalled: true);

        Result<ArtworkImage> result = await CreateProvider(handler, settings).GetArtworkAsync(Table());

        result.Value.Should().Be(cachedImage);
    }

    [Fact]
    public async Task Fails_gracefully_when_the_search_returns_no_items()
    {
        var settings = new FakeSettingsService { ApiKey = "test-key", EngineId = "test-cx" };
        var handler = new RoutingHandler(searchResponseJson: """{ "items": [] }""");

        Result<ArtworkImage> result = await CreateProvider(handler, settings).GetArtworkAsync(Table());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_gracefully_when_the_search_request_itself_fails()
    {
        var settings = new FakeSettingsService { ApiKey = "test-key", EngineId = "test-cx" };
        var handler = new RoutingHandler(searchStatusCode: HttpStatusCode.Forbidden);

        Result<ArtworkImage> result = await CreateProvider(handler, settings).GetArtworkAsync(Table());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Includes_the_manufacturer_in_the_search_query_when_known()
    {
        var settings = new FakeSettingsService { ApiKey = "test-key", EngineId = "test-cx" };
        var handler = new RoutingHandler(searchResponseJson: """{ "items": [] }""");

        await CreateProvider(handler, settings).GetArtworkAsync(Table(manufacturer: "Williams"));

        handler.LastSearchUrl.Should().Contain(Uri.EscapeDataString("Williams"));
    }

    private GoogleCustomSearchArtworkProvider CreateProvider(HttpMessageHandler handler, FakeSettingsService settings) => new(
        _cache,
        new HttpClient(handler),
        settings,
        new PathRedactor("TestUser"),
        NullLogger<GoogleCustomSearchArtworkProvider>.Instance);

    private static VpxTableFile Table(string? manufacturer = null) => new()
    {
        Path = TablePath,
        FileName = "Medieval Madness.vpx",
        FileSizeBytes = 1,
        TableInfo = TableInfoMetadata.Empty,
        FilenameHints = FilenameHints.Empty,
        DisplayTitle = "Medieval Madness",
        DisplayManufacturer = manufacturer,
        Confidence = Confidence.High,
        Evidence = DetectionEvidence.Empty()
    };

    private static byte[] BuildJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    /// <summary>Routes a request to either the "search" response or the "image download" response based on the URL, so one handler covers both calls this provider makes.</summary>
    private sealed class RoutingHandler(
        string? searchResponseJson = null,
        byte[]? imageBytes = null,
        HttpStatusCode searchStatusCode = HttpStatusCode.OK,
        bool NeverCalled = false) : HttpMessageHandler
    {
        public int SearchRequestCount { get; private set; }

        public string? LastSearchUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (NeverCalled)
            {
                throw new InvalidOperationException("The network should not have been called for this scenario.");
            }

            string url = request.RequestUri!.ToString();

            if (url.Contains("customsearch.googleapis.com", StringComparison.OrdinalIgnoreCase))
            {
                SearchRequestCount++;
                LastSearchUrl = url;

                var response = new HttpResponseMessage(searchStatusCode);
                if (searchResponseJson is not null)
                {
                    response.Content = new StringContent(searchResponseJson, Encoding.UTF8, "application/json");
                }

                return Task.FromResult(response);
            }

            // Anything else is the direct image download.
            var imageResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(imageBytes ?? [])
            };
            return Task.FromResult(imageResponse);
        }
    }

    private sealed class FakeArtworkCache : IArtworkCache
    {
        public Dictionary<string, ArtworkImage> Stored { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<ArtworkImage?> TryGetAsync(string sourceName, string tablePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.TryGetValue(tablePath, out ArtworkImage? image) ? image : null);

        public Task SaveAsync(string sourceName, string tablePath, ArtworkImage image, CancellationToken cancellationToken = default)
        {
            Stored[tablePath] = image;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public string? ApiKey { get; set; }

        public string? EngineId { get; set; }

        public string SettingsFilePath => @"D:\Nudge\settings.json";

        public Task<NudgeSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new NudgeSettings
        {
            GoogleCustomSearchApiKey = ApiKey,
            GoogleCustomSearchEngineId = EngineId
        });

        public Task SaveAsync(NudgeSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
