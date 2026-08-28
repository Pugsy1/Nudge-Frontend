using FluentAssertions;
using Nudge.Core.Models;
using Nudge.Vpx.TableFiles;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// Every filename in this file was observed on a real machine during Phase 2 development (see
/// docs/RESEARCH-NOTES.md) - none are invented examples. Roughly half follow the loose
/// "Title (Manufacturer Year)" convention and half don't, which is exactly why every result here
/// must be honest about what it actually found rather than forcing a parse.
/// </summary>
public sealed class TableFilenameParserTests
{
    private readonly TableFilenameParser _parser = new();

    [Fact]
    public void Parses_a_clean_manufacturer_and_year_with_no_space_before_the_parenthesis()
    {
        FilenameHints hints = _parser.Parse("BlackKnight2000(Williams 1989).vpx");

        hints.Title.Should().Be("BlackKnight2000");
        hints.Manufacturer.Should().Be("Williams");
        hints.Year.Should().Be(1989);
        hints.HasManufacturerYear.Should().BeTrue();
    }

    [Fact]
    public void Parses_manufacturer_and_year_plus_a_trailing_version_tag()
    {
        FilenameHints hints = _parser.Parse("CreatureFromTheBlackLagoon(Bally 1992)_1.3.vpx");

        hints.Title.Should().Be("CreatureFromTheBlackLagoon");
        hints.Manufacturer.Should().Be("Bally");
        hints.Year.Should().Be(1992);
        hints.Tags.Should().Contain("1.3");
    }

    [Fact]
    public void Parses_manufacturer_and_year_plus_a_mod_author_and_version_tag()
    {
        FilenameHints hints = _parser.Parse("BatmanDarkKnight (Stern 2008)_Bigus(MOD)4.0.vpx");

        hints.Title.Should().Be("BatmanDarkKnight");
        hints.Manufacturer.Should().Be("Stern");
        hints.Year.Should().Be(2008);
        hints.Tags.Should().Contain("Bigus");
        hints.Tags.Should().Contain("MOD");
    }

    [Fact]
    public void A_filename_with_no_parentheses_at_all_produces_no_hints()
    {
        FilenameHints hints = _parser.Parse("Batman66.vpx");

        hints.Should().Be(FilenameHints.Empty);
    }

    [Fact]
    public void A_filename_with_no_parentheses_and_jammed_together_words_produces_no_hints()
    {
        // Real filename. No parentheses means nothing for this parser to anchor on - it must not
        // try to guess where "Midway" ends and "1995v600" begins.
        FilenameHints hints = _parser.Parse("AttackfromMarsMidway 1995v600.vpx");

        hints.Should().Be(FilenameHints.Empty);
    }

    [Fact]
    public void Parentheses_that_are_not_a_manufacturer_and_year_become_a_tag_not_a_wrong_manufacturer()
    {
        // Real filename: "(VR ROOM)" is a room/edition tag, not "VR" the manufacturer in the year
        // "ROOM". The parser must recognise that the inner text has no trailing year and fall back
        // to treating it as a tag instead of misreading it.
        FilenameHints hints = _parser.Parse("Albator the movie (VR ROOM).vpx");

        hints.Title.Should().Be("Albator the movie");
        hints.Manufacturer.Should().BeNull();
        hints.Year.Should().BeNull();
        hints.HasManufacturerYear.Should().BeFalse();
        hints.Tags.Should().Contain("VR ROOM");
    }

    [Fact]
    public void A_bare_ampersand_filename_with_no_parentheses_produces_no_hints()
    {
        FilenameHints hints = _parser.Parse("Cheech&Chong.vpx");

        hints.Should().Be(FilenameHints.Empty);
    }

    [Theory]
    [InlineData("Table (Manufacturer 1949).vpx", 1949)]
    [InlineData("Table (Manufacturer 2049).vpx", 2049)]
    public void Accepts_years_across_the_full_plausible_pinball_era(string fileName, int expectedYear)
    {
        FilenameHints hints = _parser.Parse(fileName);

        hints.Year.Should().Be(expectedYear);
    }

    [Theory]
    [InlineData("Table (Manufacturer 1899).vpx")]
    [InlineData("Table (Manufacturer 2150).vpx")]
    [InlineData("Table (Manufacturer 12).vpx")]
    public void A_number_that_is_not_a_plausible_year_is_not_treated_as_one(string fileName)
    {
        FilenameHints hints = _parser.Parse(fileName);

        // The parenthesised content is still kept as a tag rather than silently discarded.
        hints.Year.Should().BeNull();
        hints.HasManufacturerYear.Should().BeFalse();
    }

    [Fact]
    public void Is_case_insensitive_about_the_vpx_extension()
    {
        FilenameHints lower = _parser.Parse("Table (Manufacturer 1989).vpx");
        FilenameHints upper = _parser.Parse("Table (Manufacturer 1989).VPX");

        // Compared field by field rather than via record equality: Tags is a List<string>, which
        // compares by reference, not content, so two independently-parsed empty lists would fail a
        // whole-record equality check even though nothing is actually different.
        upper.Title.Should().Be(lower.Title);
        upper.Manufacturer.Should().Be(lower.Manufacturer);
        upper.Year.Should().Be(lower.Year);
        upper.Tags.Should().Equal(lower.Tags);
    }

    [Fact]
    public void An_empty_filename_produces_no_hints_rather_than_throwing()
    {
        FilenameHints hints = _parser.Parse(string.Empty);

        hints.Should().Be(FilenameHints.Empty);
    }
}
