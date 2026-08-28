using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Nudge.TestSupport;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// Reconciling a table's OLE metadata against its filename. The governing rule, taken directly
/// from docs/RESEARCH-NOTES.md: table metadata is frequently stale because most circulating tables
/// are mods of mods, so the filename wins on disagreement - but nothing is thrown away, and every
/// disagreement is recorded as evidence rather than silently resolved.
/// </summary>
public sealed class VpxTableFileReaderTests
{
    [Fact]
    public async Task When_metadata_and_filename_agree_confidence_is_High()
    {
        byte[] bytes = SyntheticVpxFile.Build(tableName: "Black Knight 2000");
        Result<VpxTableFile> result = await ReadAsync(bytes, "BlackKnight2000(Williams 1989).vpx");

        result.IsSuccess.Should().BeTrue();
        result.Value.DisplayTitle.Should().Be("BlackKnight2000");
        result.Value.Confidence.Should().Be(Confidence.High);
    }

    /// <summary>
    /// The real case observed during Phase 2 development: a table whose internal metadata still
    /// says "Strange Science" while the file has been renamed "Breaking Badv2.vpx" through a chain
    /// of mods. The filename must win, and the disagreement must not be hidden.
    /// </summary>
    [Fact]
    public async Task When_metadata_and_filename_disagree_the_filename_wins_but_the_conflict_is_recorded()
    {
        byte[] bytes = SyntheticVpxFile.Build(tableName: "Strange Science");
        Result<VpxTableFile> result = await ReadAsync(bytes, "Breaking Bad (Konami 1999).vpx");

        result.Value.DisplayTitle.Should().Be("Breaking Bad");
        result.Value.TableInfo.TableName.Should().Be("Strange Science", "the raw OLE value must still be kept, not discarded");
        result.Value.Confidence.Should().Be(Confidence.Medium);
        result.Value.Evidence.Summary.Should().Contain("Strange Science").And.Contain("Breaking Bad");
    }

    [Fact]
    public async Task When_only_the_filename_has_a_title_it_is_used_at_Medium_confidence()
    {
        byte[] bytes = SyntheticVpxFile.Build();
        Result<VpxTableFile> result = await ReadAsync(bytes, "Cool Table (Bally 1975).vpx");

        result.Value.DisplayTitle.Should().Be("Cool Table");
        result.Value.Confidence.Should().Be(Confidence.Medium);
    }

    [Fact]
    public async Task When_only_the_metadata_has_a_title_it_is_used_at_Medium_confidence()
    {
        byte[] bytes = SyntheticVpxFile.Build(tableName: "Metadata Only Title");
        Result<VpxTableFile> result = await ReadAsync(bytes, "no_pattern_here.vpx");

        result.Value.DisplayTitle.Should().Be("Metadata Only Title");
        result.Value.Confidence.Should().Be(Confidence.Medium);
    }

    [Fact]
    public async Task When_neither_source_has_a_title_the_raw_filename_is_used_at_Low_confidence()
    {
        byte[] bytes = SyntheticVpxFile.Build();
        Result<VpxTableFile> result = await ReadAsync(bytes, "no_pattern_here.vpx");

        result.Value.DisplayTitle.Should().Be("no_pattern_here");
        result.Value.Confidence.Should().Be(Confidence.Low);
    }

    [Fact]
    public async Task Manufacturer_and_year_come_from_the_filename_and_are_carried_onto_the_result()
    {
        byte[] bytes = SyntheticVpxFile.Build(tableName: "Creature");
        Result<VpxTableFile> result = await ReadAsync(bytes, "Creature(Bally 1992)_1.3.vpx");

        result.Value.DisplayManufacturer.Should().Be("Bally");
        result.Value.DisplayYear.Should().Be(1992);
    }

    [Fact]
    public async Task File_size_is_populated_from_the_real_file()
    {
        byte[] bytes = SyntheticVpxFile.Build(tableName: "Sized");
        Result<VpxTableFile> result = await ReadAsync(bytes, "Sized.vpx");

        result.Value.FileSizeBytes.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task A_file_that_is_not_a_readable_vpx_fails_rather_than_producing_a_guess()
    {
        byte[] bytes = SyntheticVpxFile.NotAnOleFile();
        Result<VpxTableFile> result = await ReadAsync(bytes, "junk.vpx");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task A_missing_file_fails_rather_than_throwing()
    {
        var harness = new NudgeTestHarness(new MockFileSystem());

        Result<VpxTableFile> result = await harness.BuildTableFileReader().ReadAsync(@"D:\Tables\gone.vpx");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Every_result_carries_evidence()
    {
        byte[] bytes = SyntheticVpxFile.Build(tableName: "Evidenced");
        Result<VpxTableFile> result = await ReadAsync(bytes, "Evidenced.vpx");

        result.Value.Evidence.Should().NotBeEmpty();
    }

    private static async Task<Result<VpxTableFile>> ReadAsync(byte[] fileBytes, string fileName)
    {
        var fileSystem = new MockFileSystem();
        string path = fileSystem.Path.Combine(@"D:\Tables", fileName);
        fileSystem.AddFile(path, new MockFileData(fileBytes));

        var harness = new NudgeTestHarness(fileSystem);
        return await harness.BuildTableFileReader().ReadAsync(path);
    }
}
