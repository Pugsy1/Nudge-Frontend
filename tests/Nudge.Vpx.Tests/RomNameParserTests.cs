using FluentAssertions;
using Nudge.Core.Models;
using Nudge.Vpx.TableFiles;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// <see cref="RomNameParser"/> searches a table's VBScript text for its PinMAME ROM name. Every
/// scenario here mirrors a real shape found scanning the maintainer's actual table collection while
/// building this reader - see docs/RESEARCH-NOTES.md.
/// </summary>
public sealed class RomNameParserTests
{
    private readonly RomNameParser _parser = new();

    [Fact]
    public void A_single_clean_assignment_is_High_confidence()
    {
        RomNameInfo result = _parser.Parse("Const cGameName = \"bk2k_l4\"\r\n.GameName = cGameName");

        result.RomName.Should().Be("bk2k_l4");
        result.Confidence.Should().Be(Confidence.High);
    }

    [Fact]
    public void Works_without_the_Const_keyword_and_without_spaces_around_the_equals_sign()
    {
        RomNameInfo result = _parser.Parse("cGameName=\"dof_test\"");

        result.RomName.Should().Be("dof_test");
        result.Confidence.Should().Be(Confidence.High);
    }

    /// <summary>
    /// The real case found in Medieval Madness: three commented-out alternative ROM revisions and
    /// one live assignment. The commented lines must never win just because they appear first.
    /// </summary>
    [Fact]
    public void Commented_out_alternatives_are_ignored_even_when_they_come_first()
    {
        const string script = """
            'Const cGameName = "mm_10" 		'Williams Official V1.0 ROM
            'Const cGameName = "mm_109" 	'Unofficial V1.09 Free Play ROM
            Const cGameName="mm_109b" 		'Unofficial V1.09 ROM
            'Const cGameName="mm_109c" 		'Unofficial V1.09 Profanity ROM
            .GameName = cGameName
            """;

        RomNameInfo result = _parser.Parse(script);

        result.RomName.Should().Be("mm_109b");
        result.Confidence.Should().Be(Confidence.High, "only one uncommented assignment exists");
    }

    [Fact]
    public void Multiple_uncommented_assignments_use_the_first_at_Medium_confidence()
    {
        const string script = """
            Const cGameName = "first_rom"
            Const cGameName = "second_rom"
            """;

        RomNameInfo result = _parser.Parse(script);

        result.RomName.Should().Be("first_rom");
        result.Confidence.Should().Be(Confidence.Medium);
        result.Evidence.Summary.Should().Contain("first_rom").And.Contain("second_rom");
    }

    /// <summary>
    /// The real case found in Twilight Zone: cGameName is a plain Dim'd variable assigned inside a
    /// Select Case block depending on a runtime setting, never as a single top-level constant. Nudge
    /// does not evaluate script logic, so this must report "not found" rather than guess a branch.
    /// </summary>
    [Fact]
    public void A_conditionally_assigned_ROM_name_is_reported_as_not_found_not_guessed()
    {
        const string script = """
            Dim cGameName
            Select Case RomVersion
            Case 0: cGameName = "tz_94h"
            Case 1: cGameName = "tz_94ch"
            End Select
            .GameName = cGameName
            """;

        RomNameInfo result = _parser.Parse(script);

        result.RomName.Should().BeNull();
        result.Confidence.Should().Be(Confidence.Unknown);
    }

    [Fact]
    public void An_empty_script_is_reported_as_not_found()
    {
        RomNameInfo result = _parser.Parse(string.Empty);

        result.RomName.Should().BeNull();
        result.Confidence.Should().Be(Confidence.Unknown);
    }

    [Fact]
    public void Every_result_carries_evidence()
    {
        _parser.Parse("Const cGameName = \"anything\"").Evidence.Should().NotBeEmpty();
        _parser.Parse(string.Empty).Evidence.Should().NotBeEmpty();
    }
}
