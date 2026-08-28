using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Nudge.TestSupport;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// Architecture must come from the PE header. These tests run the real PEReader against real,
/// structurally valid PE bytes, so they prove the header is genuinely being parsed.
/// </summary>
public sealed class PeArchitectureReaderTests
{
    [Fact]
    public async Task Reads_x64_from_the_PE_header()
    {
        var harness = BuildHarness(@"D:\vpx\anything.exe", SyntheticPortableExecutable.X64());

        Result<ProcessorArchitecture> result =
            await harness.ArchitectureReader.ReadArchitectureAsync(@"D:\vpx\anything.exe");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ProcessorArchitecture.X64);
    }

    [Fact]
    public async Task Reads_x86_from_the_PE_header()
    {
        var harness = BuildHarness(@"D:\vpx\anything.exe", SyntheticPortableExecutable.X86());

        Result<ProcessorArchitecture> result =
            await harness.ArchitectureReader.ReadArchitectureAsync(@"D:\vpx\anything.exe");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ProcessorArchitecture.X86);
    }

    /// <summary>
    /// The central claim of this component: a filename that says 64-bit does not make a 32-bit file
    /// 64-bit. This is the test that would fail if anyone ever "optimised" the reader into a
    /// filename check.
    /// </summary>
    [Fact]
    public async Task A_filename_claiming_64_bit_does_not_change_a_32_bit_answer()
    {
        const string path = @"D:\vpx\VPinballX_GL64.exe";
        var harness = BuildHarness(path, SyntheticPortableExecutable.X86());

        Result<ProcessorArchitecture> result = await harness.ArchitectureReader.ReadArchitectureAsync(path);

        result.Value.Should().Be(
            ProcessorArchitecture.X86,
            "the PE header says 32-bit and the filename is not evidence");
    }

    [Fact]
    public async Task A_filename_without_a_64_marker_does_not_change_a_64_bit_answer()
    {
        const string path = @"D:\vpx\VPinballX.exe";
        var harness = BuildHarness(path, SyntheticPortableExecutable.X64());

        Result<ProcessorArchitecture> result = await harness.ArchitectureReader.ReadArchitectureAsync(path);

        result.Value.Should().Be(ProcessorArchitecture.X64);
    }

    [Fact]
    public async Task An_unrecognised_machine_type_is_Unknown_not_a_guess()
    {
        const string path = @"D:\vpx\something.exe";
        var harness = BuildHarness(path, SyntheticPortableExecutable.UnrecognisedArchitecture());

        Result<ProcessorArchitecture> result = await harness.ArchitectureReader.ReadArchitectureAsync(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ProcessorArchitecture.Unknown);
    }

    [Fact]
    public async Task A_file_that_is_not_an_executable_fails_with_a_readable_message()
    {
        const string path = @"D:\vpx\notreally.exe";
        var harness = BuildHarness(path, SyntheticPortableExecutable.NotAnExecutable());

        Result<ProcessorArchitecture> result = await harness.ArchitectureReader.ReadArchitectureAsync(path);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("notreally.exe");
    }

    [Fact]
    public async Task A_missing_file_fails_rather_than_throwing()
    {
        var harness = new NudgeTestHarness(new MockFileSystem());

        Result<ProcessorArchitecture> result =
            await harness.ArchitectureReader.ReadArchitectureAsync(@"D:\vpx\gone.exe");

        result.IsFailure.Should().BeTrue();
    }

    private static NudgeTestHarness BuildHarness(string path, byte[] contents)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(path, new MockFileData(contents));
        return new NudgeTestHarness(fileSystem);
    }
}
