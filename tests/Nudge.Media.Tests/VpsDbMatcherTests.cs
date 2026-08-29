using FluentAssertions;
using Nudge.Core.Models;
using Nudge.Media.VpsDb;
using Xunit;

namespace Nudge.Media.Tests;

/// <summary>
/// <see cref="VpsDbMatcher"/> is a simple normalised-equality matcher, not a fuzzy one - see its own
/// remarks. These tests cover the ambiguous-title disambiguation path (manufacturer, then year) and
/// the deliberate refusal to guess when nothing normalises to an exact match.
/// </summary>
public sealed class VpsDbMatcherTests
{
    [Fact]
    public void Matches_a_single_entry_by_normalised_title()
    {
        VpxTableFile table = Table("Medieval Madness");
        var entries = new List<VpsDbEntry> { Entry("id1", "Medieval Madness") };

        VpsDbEntry? match = VpsDbMatcher.FindMatch(table, entries);

        match.Should().NotBeNull();
        match!.Id.Should().Be("id1");
    }

    [Fact]
    public void Ignores_punctuation_and_case_when_matching_titles()
    {
        VpxTableFile table = Table("BlackKnight2000");
        var entries = new List<VpsDbEntry> { Entry("id1", "Black Knight 2000!") };

        VpsDbMatcher.FindMatch(table, entries)?.Id.Should().Be("id1");
    }

    [Fact]
    public void Disambiguates_same_titled_entries_by_manufacturer()
    {
        VpxTableFile table = Table("Twilight Zone", manufacturer: "Bally");
        var entries = new List<VpsDbEntry>
        {
            Entry("wrong-manufacturer", "Twilight Zone", manufacturer: "Data East"),
            Entry("right-manufacturer", "Twilight Zone", manufacturer: "Bally")
        };

        VpsDbMatcher.FindMatch(table, entries)?.Id.Should().Be("right-manufacturer");
    }

    [Fact]
    public void Falls_back_to_year_when_manufacturer_does_not_narrow_it_down()
    {
        VpxTableFile table = Table("Medieval Madness", manufacturer: "Williams", year: 1997);
        var entries = new List<VpsDbEntry>
        {
            Entry("wrong-year", "Medieval Madness", manufacturer: "Williams", year: 1996),
            Entry("right-year", "Medieval Madness", manufacturer: "Williams", year: 1997)
        };

        VpsDbMatcher.FindMatch(table, entries)?.Id.Should().Be("right-year");
    }

    [Fact]
    public void Returns_null_rather_than_a_nearest_neighbour_when_nothing_matches()
    {
        VpxTableFile table = Table("A Table Nobody Has Heard Of");
        var entries = new List<VpsDbEntry> { Entry("id1", "Completely Different Table") };

        VpsDbMatcher.FindMatch(table, entries).Should().BeNull();
    }

    [Fact]
    public void Returns_null_for_an_empty_index()
    {
        VpsDbMatcher.FindMatch(Table("Anything"), []).Should().BeNull();
    }

    [Fact]
    public void BestImageUrl_prefers_a_table_screenshot_over_a_backglass()
    {
        VpsDbEntry entry = Entry("id1", "Any Table");
        entry.TableFiles.Add(new VpsDbMediaFile { ImgUrl = "https://example.test/table.webp" });
        entry.B2SFiles.Add(new VpsDbMediaFile { ImgUrl = "https://example.test/backglass.webp" });

        string? url = VpsDbMatcher.BestImageUrl(entry, out string source);

        url.Should().Be("https://example.test/table.webp");
        source.Should().Contain("Table");
    }

    [Fact]
    public void BestImageUrl_falls_back_to_a_backglass_when_there_is_no_table_screenshot()
    {
        VpsDbEntry entry = Entry("id1", "Any Table");
        entry.B2SFiles.Add(new VpsDbMediaFile { ImgUrl = "https://example.test/backglass.webp" });

        string? url = VpsDbMatcher.BestImageUrl(entry, out string source);

        url.Should().Be("https://example.test/backglass.webp");
        source.Should().Contain("Backglass");
    }

    [Fact]
    public void BestImageUrl_is_null_when_the_matched_entry_has_no_image_at_all()
    {
        VpsDbEntry entry = Entry("id1", "Any Table");

        VpsDbMatcher.BestImageUrl(entry, out _).Should().BeNull();
    }

    private static VpxTableFile Table(string title, string? manufacturer = null, int? year = null) => new()
    {
        Path = $@"D:\Tables\{title}.vpx",
        FileName = $"{title}.vpx",
        FileSizeBytes = 1,
        TableInfo = TableInfoMetadata.Empty,
        FilenameHints = FilenameHints.Empty,
        DisplayTitle = title,
        DisplayManufacturer = manufacturer,
        DisplayYear = year,
        Confidence = Confidence.High,
        Evidence = DetectionEvidence.Empty()
    };

    private static VpsDbEntry Entry(string id, string name, string? manufacturer = null, int? year = null) => new()
    {
        Id = id,
        Name = name,
        Manufacturer = manufacturer,
        Year = year
    };
}
