using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Nudge.Core.Results;
using Nudge.TestSupport;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// <see cref="Nudge.Vpx.TableFiles.GameDataScriptReader"/> extracts the raw script text from
/// <c>GameStg\GameData</c>'s "CODE" BIFF record. The binary format itself is not documented by
/// vpinball; see the class's own remarks and docs/RESEARCH-NOTES.md for how it was verified.
/// </summary>
public sealed class GameDataScriptReaderTests
{
    [Fact]
    public async Task Reads_the_script_out_of_a_real_shaped_GameData_stream()
    {
        const string script = "Dim x\r\nConst cGameName = \"mm_109b\"\r\nSub Foo()\r\nEnd Sub";
        byte[] bytes = SyntheticVpxFile.Build(tableName: "Medieval Madness", gameScript: script);

        Result<string> result = await ReadAsync(bytes);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(script);
    }

    [Fact]
    public async Task A_table_with_no_GameStg_storage_at_all_returns_an_empty_script_not_a_failure()
    {
        byte[] bytes = SyntheticVpxFile.Build(tableName: "No GameStg", includeGameStg: false);

        Result<string> result = await ReadAsync(bytes);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task A_GameStg_with_no_GameData_stream_returns_an_empty_script_not_a_failure()
    {
        // includeGameStg without a gameScript writes a GameStg\GameData-less GameStg (only a
        // "Version" stream), matching a shape SyntheticVpxFile has always produced.
        byte[] bytes = SyntheticVpxFile.Build(tableName: "No Script", includeGameStg: true);

        Result<string> result = await ReadAsync(bytes);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_that_is_not_a_readable_vpx_fails_rather_than_producing_a_guess()
    {
        byte[] bytes = SyntheticVpxFile.NotAnOleFile();

        Result<string> result = await ReadAsync(bytes);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task A_missing_file_fails_rather_than_throwing()
    {
        var harness = new NudgeTestHarness(new MockFileSystem());

        Result<string> result = await harness.GameDataScriptReader.ReadScriptAsync(@"D:\Tables\gone.vpx");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task An_empty_script_string_round_trips_as_empty()
    {
        byte[] bytes = SyntheticVpxFile.Build(tableName: "Empty Script", gameScript: string.Empty);

        Result<string> result = await ReadAsync(bytes);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    private static async Task<Result<string>> ReadAsync(byte[] fileBytes)
    {
        var fileSystem = new MockFileSystem();
        string path = fileSystem.Path.Combine(@"D:\Tables", "Table.vpx");
        fileSystem.AddFile(path, new MockFileData(fileBytes));

        var harness = new NudgeTestHarness(fileSystem);
        return await harness.GameDataScriptReader.ReadScriptAsync(path);
    }
}
