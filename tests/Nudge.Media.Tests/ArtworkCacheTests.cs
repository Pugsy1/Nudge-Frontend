using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Xunit;

namespace Nudge.Media.Tests;

public sealed class ArtworkCacheTests
{
    private const string CacheDirectory = @"D:\Nudge\artwork\images";
    private const string TablePath = @"D:\VPX\Tables\Medieval Madness.vpx";
    private const string SourceName = "vps-db";

    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public async Task Nothing_is_cached_for_a_table_that_has_never_been_saved()
    {
        ArtworkImage? result = await CreateCache().TryGetAsync(SourceName, TablePath);

        result.Should().BeNull();
    }

    [Fact]
    public async Task A_saved_image_round_trips_exactly()
    {
        var cache = CreateCache();
        var image = new ArtworkImage
        {
            Data = [1, 2, 3, 4, 5],
            Width = 300,
            Height = 200,
            Source = "Table image (vps-db)"
        };

        await cache.SaveAsync(SourceName, TablePath, image);
        ArtworkImage? result = await cache.TryGetAsync(SourceName, TablePath);

        result.Should().NotBeNull();
        result!.Data.Should().Equal(image.Data);
        result.Width.Should().Be(300);
        result.Height.Should().Be(200);
        result.Source.Should().Be("Table image (vps-db)");
    }

    [Fact]
    public async Task Different_table_paths_get_different_cache_entries()
    {
        var cache = CreateCache();
        await cache.SaveAsync(SourceName, TablePath, new ArtworkImage { Data = [1], Width = 1, Height = 1, Source = "A" });
        await cache.SaveAsync(SourceName, @"D:\VPX\Tables\Other Table.vpx", new ArtworkImage { Data = [2], Width = 2, Height = 2, Source = "B" });

        ArtworkImage? first = await cache.TryGetAsync(SourceName, TablePath);
        ArtworkImage? second = await cache.TryGetAsync(SourceName, @"D:\VPX\Tables\Other Table.vpx");

        first!.Data.Should().Equal([1]);
        second!.Data.Should().Equal([2]);
    }

    [Fact]
    public async Task Table_path_comparison_is_case_insensitive()
    {
        var cache = CreateCache();
        await cache.SaveAsync(SourceName, TablePath, new ArtworkImage { Data = [9], Width = 1, Height = 1, Source = "A" });

        ArtworkImage? result = await cache.TryGetAsync(SourceName, TablePath.ToUpperInvariant());

        result.Should().NotBeNull("the same table looked up with different casing must hit the same cache entry");
    }

    [Fact]
    public async Task Different_sources_for_the_same_table_get_independent_cache_entries()
    {
        // The reason source name is part of the key at all: switching a table from one source to
        // another must never keep silently serving the first source's cached image back out.
        var cache = CreateCache();
        await cache.SaveAsync("vps-db", TablePath, new ArtworkImage { Data = [1], Width = 1, Height = 1, Source = "vps-db" });
        await cache.SaveAsync("Google Images", TablePath, new ArtworkImage { Data = [2], Width = 2, Height = 2, Source = "Google Images" });

        ArtworkImage? fromVpsDb = await cache.TryGetAsync("vps-db", TablePath);
        ArtworkImage? fromGoogle = await cache.TryGetAsync("Google Images", TablePath);

        fromVpsDb!.Data.Should().Equal([1]);
        fromGoogle!.Data.Should().Equal([2]);
    }

    private ArtworkCache CreateCache() => new(
        _fileSystem,
        CacheDirectory,
        new PathRedactor("TestUser"),
        NullLogger<ArtworkCache>.Instance);
}
