using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Media.VpsDb;
using Xunit;

namespace Nudge.Media.Tests;

public sealed class VpsDbIndexTests
{
    private const string CachePath = @"D:\Nudge\artwork\vpsdb-index.json";

    private const string SampleJson = """
        [ { "id": "id1", "name": "Medieval Madness", "manufacturer": "Williams", "year": 1997,
            "tableFiles": [], "b2sFiles": [] } ]
        """;

    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public async Task Downloads_and_caches_the_index_when_nothing_is_cached_yet()
    {
        var handler = new FakeHttpMessageHandler(SampleJson);
        IVpsDbIndex index = CreateIndex(handler);

        IReadOnlyList<VpsDbEntry> entries = await index.GetEntriesAsync();

        entries.Should().ContainSingle().Which.Name.Should().Be("Medieval Madness");
        handler.RequestCount.Should().Be(1);
        _fileSystem.File.Exists(CachePath).Should().BeTrue("a successful download must be cached to disk");
    }

    [Fact]
    public async Task Uses_the_disk_cache_without_a_network_call_when_it_is_fresh()
    {
        _fileSystem.AddFile(CachePath, new MockFileData(SampleJson) { LastWriteTime = DateTime.UtcNow });
        var handler = new FakeHttpMessageHandler(SampleJson);

        IReadOnlyList<VpsDbEntry> entries = await CreateIndex(handler).GetEntriesAsync();

        entries.Should().ContainSingle();
        handler.RequestCount.Should().Be(0, "a fresh cache must never trigger a network call");
    }

    [Fact]
    public async Task Falls_back_to_a_stale_cache_when_the_network_is_unreachable()
    {
        _fileSystem.AddFile(CachePath, new MockFileData(SampleJson) { LastWriteTime = DateTime.UtcNow.AddDays(-10) });
        var handler = new FakeHttpMessageHandler(exceptionToThrow: new HttpRequestException("offline"));

        IReadOnlyList<VpsDbEntry> entries = await CreateIndex(handler).GetEntriesAsync();

        entries.Should().ContainSingle("a stale cache is still better than nothing when the network fails");
    }

    [Fact]
    public async Task Returns_an_empty_list_rather_than_throwing_when_there_is_neither_a_cache_nor_a_working_network()
    {
        var handler = new FakeHttpMessageHandler(exceptionToThrow: new HttpRequestException("offline"));

        IReadOnlyList<VpsDbEntry> entries = await CreateIndex(handler).GetEntriesAsync();

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_downloads_once_even_when_called_repeatedly()
    {
        var handler = new FakeHttpMessageHandler(SampleJson);
        IVpsDbIndex index = CreateIndex(handler);

        await index.GetEntriesAsync();
        await index.GetEntriesAsync();
        await index.GetEntriesAsync();

        handler.RequestCount.Should().Be(1, "the in-memory result should be reused for the lifetime of this instance");
    }

    private VpsDbIndex CreateIndex(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        _fileSystem,
        CachePath,
        new PathRedactor("TestUser"),
        NullLogger<VpsDbIndex>.Instance);

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly string? _responseBody;
        private readonly Exception? _exceptionToThrow;

        public FakeHttpMessageHandler(string? responseBody = null, Exception? exceptionToThrow = null)
        {
            _responseBody = responseBody;
            _exceptionToThrow = exceptionToThrow;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            if (_exceptionToThrow is not null)
            {
                throw _exceptionToThrow;
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(_responseBody!))
            };
            return Task.FromResult(response);
        }
    }
}
