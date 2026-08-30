using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Xunit;

namespace Nudge.Media.Tests;

/// <summary>
/// <see cref="CompositeArtworkProvider"/> never finds artwork itself - it only decides which
/// registered <see cref="IArtworkProvider"/> to ask, and in what order. Every scenario here uses
/// fake sources that just report whether they were called and what they returned.
/// </summary>
public sealed class CompositeArtworkProviderTests
{
    private const string TablePath = @"D:\VPX\Tables\Medieval Madness.vpx";

    [Fact]
    public async Task Tries_the_default_source_first_when_no_override_exists()
    {
        var vpsDb = FakeSource.Succeeding("vps-db");
        var google = FakeSource.Succeeding("Google Images");
        var settings = new FakeSettingsService { DefaultSourceName = "vps-db" };

        Result<ArtworkImage> result = await CreateComposite([vpsDb, google], settings).GetArtworkAsync(Table());

        result.Value.Source.Should().Be("vps-db");
        google.WasCalled.Should().BeFalse("the default source already succeeded");
    }

    [Fact]
    public async Task Falls_through_to_the_next_source_when_the_default_finds_nothing()
    {
        var vpsDb = FakeSource.Failing("vps-db");
        var google = FakeSource.Succeeding("Google Images");
        var settings = new FakeSettingsService { DefaultSourceName = "vps-db" };

        Result<ArtworkImage> result = await CreateComposite([vpsDb, google], settings).GetArtworkAsync(Table());

        result.IsSuccess.Should().BeTrue();
        result.Value.Source.Should().Be("Google Images");
        vpsDb.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task A_per_table_override_is_tried_exclusively_with_no_fallback()
    {
        var vpsDb = FakeSource.Failing("vps-db");
        var google = FakeSource.Succeeding("Google Images");
        var settings = new FakeSettingsService
        {
            DefaultSourceName = "Google Images",
            Overrides = { [TablePath] = "vps-db" }
        };

        Result<ArtworkImage> result = await CreateComposite([vpsDb, google], settings).GetArtworkAsync(Table());

        result.IsFailure.Should().BeTrue("the table is pinned to vps-db specifically, which found nothing");
        vpsDb.WasCalled.Should().BeTrue();
        google.WasCalled.Should().BeFalse("an explicit per-table choice must not be second-guessed by falling back");
    }

    [Fact]
    public async Task An_override_naming_an_unregistered_source_falls_back_to_the_default_order()
    {
        var vpsDb = FakeSource.Succeeding("vps-db");
        var settings = new FakeSettingsService
        {
            DefaultSourceName = "vps-db",
            Overrides = { [TablePath] = "Some Source That Does Not Exist" }
        };

        Result<ArtworkImage> result = await CreateComposite([vpsDb], settings).GetArtworkAsync(Table());

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_when_every_source_finds_nothing()
    {
        var vpsDb = FakeSource.Failing("vps-db");
        var google = FakeSource.Failing("Google Images");
        var settings = new FakeSettingsService { DefaultSourceName = "vps-db" };

        Result<ArtworkImage> result = await CreateComposite([vpsDb, google], settings).GetArtworkAsync(Table());

        result.IsFailure.Should().BeTrue();
        vpsDb.WasCalled.Should().BeTrue();
        google.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_gracefully_when_no_sources_are_registered_at_all()
    {
        var settings = new FakeSettingsService();

        Result<ArtworkImage> result = await CreateComposite([], settings).GetArtworkAsync(Table());

        result.IsFailure.Should().BeTrue();
    }

    private static CompositeArtworkProvider CreateComposite(IEnumerable<IArtworkProvider> sources, ISettingsService settings) =>
        new(sources, settings, NullLogger<CompositeArtworkProvider>.Instance);

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

    private sealed class FakeSource(string name, bool succeeds) : IArtworkProvider
    {
        public static FakeSource Succeeding(string name) => new(name, succeeds: true);

        public static FakeSource Failing(string name) => new(name, succeeds: false);

        public bool WasCalled { get; private set; }

        public string Name => name;

        public Task<Result<ArtworkImage>> GetArtworkAsync(VpxTableFile table, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(succeeds
                ? Result<ArtworkImage>.Success(new ArtworkImage { Data = [1], Width = 1, Height = 1, Source = name })
                : Result<ArtworkImage>.Failure("not found"));
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public string DefaultSourceName { get; set; } = "vps-db";

        public Dictionary<string, string> Overrides { get; } = [];

        public string SettingsFilePath => @"D:\Nudge\settings.json";

        public Task<NudgeSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new NudgeSettings
        {
            DefaultArtworkSourceName = DefaultSourceName,
            TableArtworkSourceOverrides = Overrides
        });

        public Task SaveAsync(NudgeSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task MutateAsync(Action<NudgeSettings> mutate, CancellationToken cancellationToken = default)
        {
            mutate(new NudgeSettings { DefaultArtworkSourceName = DefaultSourceName, TableArtworkSourceOverrides = Overrides });
            return Task.CompletedTask;
        }
    }
}
