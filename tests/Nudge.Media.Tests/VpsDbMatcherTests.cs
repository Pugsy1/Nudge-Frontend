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
    public void Strips_a_VR_Room_naming_prefix_that_is_not_part_of_the_real_title()
    {
        // The real case found scanning the maintainer's collection: "VR ROOM Attack from Mars" is a
        // VR conversion author's naming convention, not part of Attack from Mars's actual title.
        VpxTableFile table = Table("VR ROOM Attack from Mars");
        var entries = new List<VpsDbEntry> { Entry("id1", "Attack from Mars") };

        VpsDbMatcher.FindMatch(table, entries)?.Id.Should().Be("id1");
    }

    [Fact]
    public void Matches_an_edition_suffix_against_a_base_entry_with_no_suffix()
    {
        VpxTableFile table = Table("Game of Thrones LE");
        var entries = new List<VpsDbEntry> { Entry("id1", "Game of Thrones") };

        VpsDbMatcher.FindMatch(table, entries)?.Id.Should().Be("id1");
    }

    [Fact]
    public void Splits_a_concatenated_camelCase_title_the_same_way_a_spaced_one_would()
    {
        // The real case: the filename-derived title has no separators at all, but vps-db's entry
        // also has an extra stopword ("The") that a plain normalised-string match would still miss.
        VpxTableFile table = Table("BatmanDarkKnight");
        var entries = new List<VpsDbEntry> { Entry("id1", "Batman: The Dark Knight") };

        VpsDbMatcher.FindMatch(table, entries)?.Id.Should().Be("id1");
    }

    [Fact]
    public void Splits_a_trailing_year_glued_directly_onto_a_word_with_no_separator()
    {
        // A real regression caught by re-measuring against the maintainer's real tables after the
        // first tokenizer pass: camelCase splitting alone turns "BlackKnight2000" into "Black" +
        // "Knight2000" (the digit run stays glued to the word before it), which then shares no token
        // with vps-db's separately-tokenized "knight"/"2000" and silently stops matching a table
        // that used to match under the old plain-normalised comparison.
        VpxTableFile table = Table("BlackKnight2000");
        var entries = new List<VpsDbEntry> { Entry("id1", "Black Knight 2000") };

        VpsDbMatcher.FindMatch(table, entries)?.Id.Should().Be("id1");
    }

    [Fact]
    public void A_short_generic_title_never_subset_matches_into_an_unrelated_longer_one()
    {
        // Guards the token-subset rule: a table simply called "Mars" must never match "Attack from
        // Mars" just because its one significant word happens to appear there too.
        VpxTableFile table = Table("Mars");
        var entries = new List<VpsDbEntry> { Entry("id1", "Attack from Mars") };

        VpsDbMatcher.FindMatch(table, entries).Should().BeNull();
    }

    [Fact]
    public void A_two_word_title_can_still_subset_match_into_a_longer_one()
    {
        VpxTableFile table = Table("Big Bang Bar");
        var entries = new List<VpsDbEntry> { Entry("id1", "VR Room Big Bang Bar") };

        VpsDbMatcher.FindMatch(table, entries)?.Id.Should().Be("id1");
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

    [Fact]
    public void FindAllMatches_returns_every_ambiguous_entry_unranked_for_browsing()
    {
        // FindMatch (the automatic path) would disambiguate down to one; FindAllMatches (the
        // browsing path - the user wants to see every plausible option and choose) must not.
        VpxTableFile table = Table("Twilight Zone");
        var entries = new List<VpsDbEntry>
        {
            Entry("bally-version", "Twilight Zone", manufacturer: "Bally"),
            Entry("mod-version", "Twilight Zone", manufacturer: "Bally VPW Mod")
        };

        List<VpsDbEntry> matches = VpsDbMatcher.FindAllMatches(table, entries);

        matches.Should().HaveCount(2);
    }

    [Fact]
    public void FindAllMatches_returns_an_empty_list_rather_than_null_when_nothing_matches()
    {
        VpsDbMatcher.FindAllMatches(Table("Anything"), []).Should().BeEmpty();
    }

    [Fact]
    public void AllImageUrls_returns_every_table_image_and_every_backglass()
    {
        VpsDbEntry entry = Entry("id1", "Medieval Madness");
        entry.TableFiles.Add(new VpsDbMediaFile { ImgUrl = "https://example.test/table1.webp" });
        entry.TableFiles.Add(new VpsDbMediaFile { ImgUrl = "https://example.test/table2.webp" });
        entry.B2SFiles.Add(new VpsDbMediaFile { ImgUrl = "https://example.test/backglass.webp" });

        List<(string Url, string Description)> urls = VpsDbMatcher.AllImageUrls(entry).ToList();

        urls.Should().HaveCount(3);
        urls.Select(u => u.Url).Should().Contain(
        [
            "https://example.test/table1.webp",
            "https://example.test/table2.webp",
            "https://example.test/backglass.webp"
        ]);
    }

    [Fact]
    public void AllImageUrls_skips_entries_with_no_ImgUrl_set()
    {
        VpsDbEntry entry = Entry("id1", "Medieval Madness");
        entry.TableFiles.Add(new VpsDbMediaFile { ImgUrl = null });

        VpsDbMatcher.AllImageUrls(entry).Should().BeEmpty();
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
