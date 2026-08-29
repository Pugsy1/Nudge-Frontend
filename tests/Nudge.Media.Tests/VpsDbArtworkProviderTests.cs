using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Nudge.Media.VpsDb;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nudge.Media.Tests;

/// <summary>
/// End-to-end orchestration: cache-first, then settings-gated, then index lookup, then a real
/// resize through <see cref="ImageResizer"/> of a real generated image over a faked HTTP response -
/// only the network transport and the index/cache/settings I/O boundaries are faked.
/// </summary>
public sealed class VpsDbArtworkProviderTests
{
    private const string TablePath = @"D:\VPX\Tables\Medieval Madness.vpx";

    private readonly FakeArtworkCache _cache = new();
    private readonly FakeSettingsService _settings = new() { FetchEnabled = true };

    [Fact]
    public async Task Returns_the_cached_image_without_consulting_the_index_or_settings()
    {
        var cachedImage = new ArtworkImage { Data = [1, 2, 3], Width = 10, Height = 10, Source = "cached" };
        _cache.Stored[TablePath] = cachedImage;
        _settings.FetchEnabled = false; // proves the cache hit short-circuits before this is even checked

        Result<ArtworkImage> result = await CreateProvider(new FakeVpsDbIndex([]), NeverCalledHandler())
            .GetArtworkAsync(Table());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(cachedImage);
    }

    [Fact]
    public async Task Fails_without_any_lookup_when_the_setting_is_off()
    {
        _settings.FetchEnabled = false;
        var index = new FakeVpsDbIndex([Entry()]);

        Result<ArtworkImage> result = await CreateProvider(index, NeverCalledHandler()).GetArtworkAsync(Table());

        result.IsFailure.Should().BeTrue();
        index.WasQueried.Should().BeFalse();
    }

    [Fact]
    public async Task Fails_when_no_matching_entry_exists()
    {
        var index = new FakeVpsDbIndex([]);

        Result<ArtworkImage> result = await CreateProvider(index, NeverCalledHandler()).GetArtworkAsync(Table());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_when_the_matched_entry_has_no_image()
    {
        VpsDbEntry entry = Entry();
        entry.TableFiles.Clear();
        entry.B2SFiles.Clear();
        var index = new FakeVpsDbIndex([entry]);

        Result<ArtworkImage> result = await CreateProvider(index, NeverCalledHandler()).GetArtworkAsync(Table());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Downloads_resizes_and_caches_the_matched_image()
    {
        var index = new FakeVpsDbIndex([Entry()]);
        byte[] sourceImage = BuildPng(1000, 1000);
        var handler = new RespondWithHandler(sourceImage);

        Result<ArtworkImage> result = await CreateProvider(index, handler).GetArtworkAsync(Table());

        result.IsSuccess.Should().BeTrue();
        result.Value.Width.Should().BeLessThanOrEqualTo(ImageResizer.MaxDimension);
        _cache.Stored.Should().ContainKey(TablePath, "a successful fetch must be cached for next time");
    }

    [Fact]
    public async Task A_download_failure_is_reported_as_the_ordinary_not_found_outcome()
    {
        var index = new FakeVpsDbIndex([Entry()]);
        var handler = new RespondWithHandler(null, statusCode: HttpStatusCode.NotFound);

        Result<ArtworkImage> result = await CreateProvider(index, handler).GetArtworkAsync(Table());

        result.IsFailure.Should().BeTrue();
        _cache.Stored.Should().NotContainKey(TablePath);
    }

    private VpsDbArtworkProvider CreateProvider(IVpsDbIndex index, HttpMessageHandler handler) => new(
        index,
        _cache,
        new HttpClient(handler),
        _settings,
        new PathRedactor("TestUser"),
        NullLogger<VpsDbArtworkProvider>.Instance);

    private static VpxTableFile Table() => new()
    {
        Path = TablePath,
        FileName = "Medieval Madness.vpx",
        FileSizeBytes = 1,
        TableInfo = TableInfoMetadata.Empty,
        FilenameHints = FilenameHints.Empty,
        DisplayTitle = "Medieval Madness",
        Confidence = Confidence.High,
        Evidence = DetectionEvidence.Empty()
    };

    private static VpsDbEntry Entry()
    {
        var entry = new VpsDbEntry { Id = "id1", Name = "Medieval Madness" };
        entry.TableFiles.Add(new VpsDbMediaFile { ImgUrl = "https://example.test/table.webp" });
        return entry;
    }

    private static byte[] BuildPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    private static HttpMessageHandler NeverCalledHandler() => new RespondWithHandler(null, shouldBeCalled: false);

    private sealed class RespondWithHandler(byte[]? body, HttpStatusCode statusCode = HttpStatusCode.OK, bool shouldBeCalled = true)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!shouldBeCalled)
            {
                throw new InvalidOperationException("The network should not have been called for this scenario.");
            }

            var response = new HttpResponseMessage(statusCode);
            if (body is not null)
            {
                response.Content = new ByteArrayContent(body);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class FakeArtworkCache : IArtworkCache
    {
        public Dictionary<string, ArtworkImage> Stored { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<ArtworkImage?> TryGetAsync(string tablePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.TryGetValue(tablePath, out ArtworkImage? image) ? image : null);

        public Task SaveAsync(string tablePath, ArtworkImage image, CancellationToken cancellationToken = default)
        {
            Stored[tablePath] = image;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeVpsDbIndex(IReadOnlyList<VpsDbEntry> entries) : IVpsDbIndex
    {
        public bool WasQueried { get; private set; }

        public Task<IReadOnlyList<VpsDbEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
        {
            WasQueried = true;
            return Task.FromResult(entries);
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public bool FetchEnabled { get; set; }

        public string SettingsFilePath => @"D:\Nudge\settings.json";

        public Task<NudgeSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NudgeSettings { FetchArtworkFromInternet = FetchEnabled });

        public Task SaveAsync(NudgeSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
