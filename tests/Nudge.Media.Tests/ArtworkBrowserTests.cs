using FluentAssertions;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Xunit;

namespace Nudge.Media.Tests;

/// <summary>
/// <see cref="ArtworkBrowser"/> is a thin dispatcher by source name - it never searches or resolves
/// anything itself, only routes to whichever <see cref="IArtworkCandidateSource"/> the caller named.
/// </summary>
public sealed class ArtworkBrowserTests
{
    private const string TablePath = @"D:\VPX\Tables\Medieval Madness.vpx";

    [Fact]
    public void AvailableSourceNames_lists_every_registered_sources_name()
    {
        var browser = new ArtworkBrowser([new FakeSource("vps-db"), new FakeSource("Google Images")]);

        browser.AvailableSourceNames.Should().BeEquivalentTo(["vps-db", "Google Images"]);
    }

    [Fact]
    public async Task SearchAsync_routes_to_the_source_named_by_the_caller()
    {
        var vpsDb = new FakeSource("vps-db");
        var google = new FakeSource("Google Images");
        var browser = new ArtworkBrowser([vpsDb, google]);

        await browser.SearchAsync(Table(), "Google Images");

        google.WasSearched.Should().BeTrue();
        vpsDb.WasSearched.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_fails_gracefully_for_an_unknown_source_name()
    {
        var browser = new ArtworkBrowser([new FakeSource("vps-db")]);

        Result<IReadOnlyList<ArtworkCandidate>> result = await browser.SearchAsync(Table(), "Not A Real Source");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task SelectAsync_routes_to_the_source_named_on_the_candidate_itself()
    {
        var vpsDb = new FakeSource("vps-db");
        var google = new FakeSource("Google Images");
        var browser = new ArtworkBrowser([vpsDb, google]);
        var candidate = new ArtworkCandidate { ImageUrl = "https://example.test/x.jpg", SourceName = "vps-db", Description = "x" };

        await browser.SelectAsync(Table(), candidate);

        vpsDb.WasResolved.Should().BeTrue();
        google.WasResolved.Should().BeFalse();
    }

    [Fact]
    public async Task SelectAsync_fails_gracefully_when_the_candidates_source_is_not_registered()
    {
        var browser = new ArtworkBrowser([new FakeSource("vps-db")]);
        var candidate = new ArtworkCandidate { ImageUrl = "https://example.test/x.jpg", SourceName = "Some Removed Source", Description = "x" };

        Result<ArtworkImage> result = await browser.SelectAsync(Table(), candidate);

        result.IsFailure.Should().BeTrue();
    }

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

    private sealed class FakeSource(string name) : IArtworkCandidateSource
    {
        public string Name => name;

        public bool WasSearched { get; private set; }

        public bool WasResolved { get; private set; }

        public Task<Result<IReadOnlyList<ArtworkCandidate>>> SearchCandidatesAsync(VpxTableFile table, CancellationToken cancellationToken)
        {
            WasSearched = true;
            return Task.FromResult(Result<IReadOnlyList<ArtworkCandidate>>.Success((IReadOnlyList<ArtworkCandidate>)[]));
        }

        public Task<Result<ArtworkImage>> ResolveCandidateAsync(VpxTableFile table, ArtworkCandidate candidate, CancellationToken cancellationToken)
        {
            WasResolved = true;
            return Task.FromResult(Result<ArtworkImage>.Success(new ArtworkImage { Data = [1], Width = 1, Height = 1, Source = name }));
        }
    }
}
