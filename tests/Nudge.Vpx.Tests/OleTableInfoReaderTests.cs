using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Nudge.TestSupport;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// Reads real OLE compound documents built with the same OpenMcdf library Nudge ships with (see
/// <see cref="SyntheticVpxFile"/>), verified against real table files' actual byte layout during
/// Phase 2 development: plain UTF-16LE text per stream, no length prefix, no terminator.
/// </summary>
public sealed class OleTableInfoReaderTests
{
    [Fact]
    public async Task Reads_every_TableInfo_field_that_is_present()
    {
        byte[] bytes = SyntheticVpxFile.Build(
            tableName: "Black Knight 2000",
            authorName: "lio, Flupper, UnclePaulie",
            authorEmail: "someone@example.com",
            authorWebSite: "https://vpuniverse.com/",
            releaseDate: "2-4-2022",
            tableVersion: "2.0",
            tableBlurb: "A blurb.",
            tableDescription: "A longer description.",
            tableRules: "Some rules.");

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(@"D:\Tables\BlackKnight2000.vpx", new MockFileData(bytes));
        var harness = new NudgeTestHarness(fileSystem);

        Result<TableInfoMetadata> result = await harness.OleTableInfoReader
            .ReadAsync(@"D:\Tables\BlackKnight2000.vpx");

        result.IsSuccess.Should().BeTrue();
        TableInfoMetadata metadata = result.Value;
        metadata.TableName.Should().Be("Black Knight 2000");
        metadata.AuthorName.Should().Be("lio, Flupper, UnclePaulie");
        metadata.AuthorEmail.Should().Be("someone@example.com");
        metadata.AuthorWebSite.Should().Be("https://vpuniverse.com/");
        metadata.ReleaseDate.Should().Be("2-4-2022");
        metadata.TableVersion.Should().Be("2.0");
        metadata.TableBlurb.Should().Be("A blurb.");
        metadata.TableDescription.Should().Be("A longer description.");
        metadata.TableRules.Should().Be("Some rules.");
        metadata.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task A_field_with_no_stream_at_all_is_null_not_an_error()
    {
        byte[] bytes = SyntheticVpxFile.Build(tableName: "Only A Name");

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(@"D:\Tables\Minimal.vpx", new MockFileData(bytes));
        var harness = new NudgeTestHarness(fileSystem);

        Result<TableInfoMetadata> result = await harness.OleTableInfoReader.ReadAsync(@"D:\Tables\Minimal.vpx");

        result.IsSuccess.Should().BeTrue();
        result.Value.TableName.Should().Be("Only A Name");
        result.Value.AuthorName.Should().BeNull();
        result.Value.TableVersion.Should().BeNull();
    }

    [Fact]
    public async Task A_table_with_no_metadata_at_all_reads_as_empty_not_a_failure()
    {
        byte[] bytes = SyntheticVpxFile.Build();

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(@"D:\Tables\Blank.vpx", new MockFileData(bytes));
        var harness = new NudgeTestHarness(fileSystem);

        Result<TableInfoMetadata> result = await harness.OleTableInfoReader.ReadAsync(@"D:\Tables\Blank.vpx");

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task A_valid_OLE_file_with_no_TableInfo_storage_reads_as_empty_not_a_failure()
    {
        byte[] bytes = SyntheticVpxFile.BuildWithoutTableInfo();

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(@"D:\Tables\NotATable.vpx", new MockFileData(bytes));
        var harness = new NudgeTestHarness(fileSystem);

        Result<TableInfoMetadata> result = await harness.OleTableInfoReader.ReadAsync(@"D:\Tables\NotATable.vpx");

        result.IsSuccess.Should().BeTrue("the file did open - it just isn't a VPX table");
        result.Value.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task A_file_that_is_not_an_OLE_document_at_all_fails_with_a_readable_message()
    {
        byte[] bytes = SyntheticVpxFile.NotAnOleFile();

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(@"D:\Tables\definitely-not-vpx.vpx", new MockFileData(bytes));
        var harness = new NudgeTestHarness(fileSystem);

        Result<TableInfoMetadata> result = await harness.OleTableInfoReader
            .ReadAsync(@"D:\Tables\definitely-not-vpx.vpx");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("definitely-not-vpx.vpx");
    }

    [Fact]
    public async Task A_missing_file_fails_rather_than_throwing()
    {
        var harness = new NudgeTestHarness(new MockFileSystem());

        Result<TableInfoMetadata> result = await harness.OleTableInfoReader.ReadAsync(@"D:\Tables\gone.vpx");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Round_trips_a_wide_range_of_text_including_punctuation_and_multiline_content()
    {
        const string description = "Line one.\r\nLine two with 'quotes' and \"double quotes\".\r\n\r\nEmoji-free but full of punctuation: !@#$%^&*()";
        byte[] bytes = SyntheticVpxFile.Build(tableDescription: description);

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(@"D:\Tables\Punctuation.vpx", new MockFileData(bytes));
        var harness = new NudgeTestHarness(fileSystem);

        Result<TableInfoMetadata> result = await harness.OleTableInfoReader.ReadAsync(@"D:\Tables\Punctuation.vpx");

        result.Value.TableDescription.Should().Be(description);
    }
}
