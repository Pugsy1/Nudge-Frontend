using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Nudge.TestSupport;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// End-to-end: <see cref="IRomNameReader"/> wired against a real, parseable synthetic .vpx file, the
/// same way <see cref="VpxTableFileReaderTests"/> exercises the fast-scan reader.
/// </summary>
public sealed class RomNameReaderTests
{
    [Fact]
    public async Task Finds_the_ROM_name_in_a_real_shaped_file()
    {
        byte[] bytes = SyntheticVpxFile.Build(
            tableName: "Black Knight 2000",
            gameScript: "Const cGameName = \"bk2k_l4\"\r\n.GameName = cGameName");

        Result<RomNameInfo> result = await ReadAsync(bytes);

        result.IsSuccess.Should().BeTrue();
        result.Value.RomName.Should().Be("bk2k_l4");
        result.Value.Confidence.Should().Be(Confidence.High);
    }

    [Fact]
    public async Task A_table_with_no_script_reports_no_ROM_name_rather_than_failing()
    {
        byte[] bytes = SyntheticVpxFile.Build(tableName: "No Script");

        Result<RomNameInfo> result = await ReadAsync(bytes);

        result.IsSuccess.Should().BeTrue();
        result.Value.RomName.Should().BeNull();
    }

    [Fact]
    public async Task A_file_that_is_not_a_readable_vpx_fails_rather_than_producing_a_guess()
    {
        byte[] bytes = SyntheticVpxFile.NotAnOleFile();

        Result<RomNameInfo> result = await ReadAsync(bytes);

        result.IsFailure.Should().BeTrue();
    }

    private static async Task<Result<RomNameInfo>> ReadAsync(byte[] fileBytes)
    {
        var fileSystem = new MockFileSystem();
        string path = fileSystem.Path.Combine(@"D:\Tables", "Table.vpx");
        fileSystem.AddFile(path, new MockFileData(fileBytes));

        var harness = new NudgeTestHarness(fileSystem);
        return await harness.BuildRomNameReader().ReadAsync(path);
    }
}
