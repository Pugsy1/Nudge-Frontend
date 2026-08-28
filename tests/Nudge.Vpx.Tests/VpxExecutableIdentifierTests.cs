using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Nudge.TestSupport;
using Nudge.Vpx.Identification;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// Flavor and VR classification. The governing rule is that an unclassifiable executable reports
/// Unknown; it is never rounded up to the nearest plausible answer.
/// </summary>
public sealed class VpxExecutableIdentifierTests
{
    [Theory]
    [InlineData("VPinballX.exe", VpxFlavor.DirectX9)]
    [InlineData("VPinballX64.exe", VpxFlavor.DirectX9)]
    [InlineData("VPinballX_GL.exe", VpxFlavor.OpenGL)]
    [InlineData("VPinballX_GL64.exe", VpxFlavor.OpenGL)]
    [InlineData("VPinballX_BGFX.exe", VpxFlavor.Bgfx)]
    [InlineData("VPinballX_BGFX64.exe", VpxFlavor.Bgfx)]
    public async Task Classifies_known_Visual_Pinball_10_filenames(string fileName, VpxFlavor expected)
    {
        VpxExecutable executable = await IdentifyAsync(fileName, version: "10.8.0.2058");

        executable.Flavor.Should().Be(expected);
    }

    [Theory]
    [InlineData("VPinball995.exe")]
    [InlineData("VPinball99.exe")]
    public async Task Classifies_the_Visual_Pinball_9_line_as_legacy(string fileName)
    {
        VpxExecutable executable = await IdentifyAsync(fileName, version: "9.9.5.0");

        executable.Flavor.Should().Be(VpxFlavor.VP9Legacy);
        executable.VrCapability.Should().Be(VrCapability.None);
    }

    [Theory]
    [InlineData("launcher.exe")]
    [InlineData("game.exe")]
    [InlineData("setup.exe")]
    [InlineData("Steam.exe")]
    public async Task Executables_that_are_not_Visual_Pinball_are_Unknown(string fileName)
    {
        VpxExecutable executable = await IdentifyAsync(fileName, version: null);

        executable.Flavor.Should().Be(VpxFlavor.Unknown);
        executable.Confidence.Should().Be(Confidence.Unknown);
        executable.LooksLikeVisualPinball.Should().BeFalse();
        executable.IsRecognised.Should().BeFalse();
    }

    /// <summary>
    /// "It is Visual Pinball, but I cannot tell which build" is a distinct answer from both
    /// "it is the OpenGL build" and "it is not Visual Pinball". Nudge must not collapse the three.
    /// </summary>
    [Fact]
    public async Task A_Visual_Pinball_executable_with_no_flavor_marker_is_Unknown_but_recognised_as_VPX()
    {
        VpxExecutable executable = await IdentifyAsync("VPinballSomethingNew.exe", version: "10.9.0.0");

        executable.Flavor.Should().Be(VpxFlavor.Unknown);
        executable.IsRecognised.Should().BeFalse();
        executable.LooksLikeVisualPinball.Should().BeTrue();
        executable.Confidence.Should().Be(Confidence.Unknown);
    }

    [Fact]
    public async Task Architecture_comes_from_the_PE_header_even_when_the_filename_disagrees()
    {
        VpxExecutable executable = await IdentifyAsync(
            "VPinballX_GL64.exe",
            version: "10.8.0.2058",
            architecture: ProcessorArchitecture.X86);

        executable.Architecture.Should().Be(ProcessorArchitecture.X86);
        executable.Flavor.Should().Be(VpxFlavor.OpenGL, "the flavor suffix is still meaningful");

        executable.Evidence.Should().Contain(
            item => item.Weight == EvidenceWeight.Contradicting && item.Observation.Contains("32-bit"),
            "the mismatch between the name and the header must be visible to the user");
    }

    // -------------------------------------------------------------------------------------------
    // VR capability
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_DirectX9_build_reports_no_VR()
    {
        VpxExecutable executable = await IdentifyAsync("VPinballX.exe", version: "10.8.0.2058");

        executable.VrCapability.Should().Be(VrCapability.None);
    }

    [Fact]
    public async Task The_OpenGL_build_reports_OpenVR()
    {
        VpxExecutable executable = await IdentifyAsync("VPinballX_GL64.exe", version: "10.8.0.2058");

        executable.VrCapability.Should().Be(VrCapability.OpenVR);
    }

    [Fact]
    public async Task A_BGFX_build_at_10_8_1_or_newer_reports_OpenXR()
    {
        VpxExecutable executable = await IdentifyAsync("VPinballX_BGFX.exe", version: "10.8.1.0");

        executable.VrCapability.Should().Be(VrCapability.OpenXR);
    }

    /// <summary>
    /// OpenXR arrived in the BGFX build at 10.8.1. For an older BGFX build Nudge does not know, and
    /// says so, rather than assuming either answer.
    /// </summary>
    [Fact]
    public async Task A_BGFX_build_older_than_10_8_1_reports_Unknown_VR_rather_than_guessing()
    {
        VpxExecutable executable = await IdentifyAsync("VPinballX_BGFX.exe", version: "10.8.0.2058");

        executable.VrCapability.Should().Be(VrCapability.Unknown);
    }

    [Fact]
    public async Task A_BGFX_build_with_no_readable_version_reports_Unknown_VR()
    {
        VpxExecutable executable = await IdentifyAsync("VPinballX_BGFX.exe", version: null);

        executable.Flavor.Should().Be(VpxFlavor.Bgfx, "the filename suffix still identifies the build");
        executable.VrCapability.Should().Be(VrCapability.Unknown);
    }

    // -------------------------------------------------------------------------------------------
    // Confidence and evidence
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Confidence_is_High_when_the_filename_version_and_support_libraries_all_agree()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller();
        var harness = new NudgeTestHarness(installation);

        Result<VpxExecutable> result = await harness.Identifier
            .IdentifyAsync(installation.PathTo("VPinballX_GL64.exe"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Flavor.Should().Be(VpxFlavor.OpenGL);
        result.Value.Confidence.Should().Be(Confidence.High);
    }

    /// <summary>
    /// A filename hint with nothing at all to back it up must not be presented as certain.
    /// </summary>
    [Fact]
    public async Task Confidence_is_Low_when_only_the_filename_suggests_a_flavor()
    {
        VpxExecutable executable = await IdentifyAsync("VPinballX_GL64.exe", version: null);

        executable.Flavor.Should().Be(VpxFlavor.OpenGL);
        executable.Confidence.Should().Be(Confidence.Low);
    }

    [Fact]
    public async Task A_version_resource_naming_the_flavor_overrides_a_conflicting_filename()
    {
        var fileSystem = new MockFileSystem();
        const string path = @"D:\vpx\VPinballX_GL64.exe";
        fileSystem.AddFile(path, new MockFileData(SyntheticPortableExecutable.X64()));

        var harness = new NudgeTestHarness(fileSystem);
        harness.VersionInfo.Set(path, new FileVersionDetails
        {
            ProductName = "Visual Pinball",
            FileDescription = "Visual Pinball BGFX renderer",
            FileVersion = "10.8.1.0",
            NumericFileVersion = new Version(10, 8, 1, 0)
        });

        Result<VpxExecutable> result = await harness.Identifier.IdentifyAsync(path);

        result.Value.Flavor.Should().Be(VpxFlavor.Bgfx, "the version resource is harder to fake than a filename");
        result.Value.Confidence.Should().Be(Confidence.Low, "the two sources disagree, so certainty must drop");
        result.Value.Evidence.Should().Contain(item => item.Weight == EvidenceWeight.Contradicting);
    }

    [Fact]
    public async Task Every_classification_carries_evidence()
    {
        VpxExecutable executable = await IdentifyAsync("VPinballX_GL64.exe", version: "10.8.0.2058");

        executable.Evidence.Should().NotBeEmpty();
        executable.Evidence.Summary.Should().Contain("PE header");
    }

    [Fact]
    public async Task Identifying_a_missing_file_fails_rather_than_throwing()
    {
        var harness = new NudgeTestHarness(new MockFileSystem());

        Result<VpxExecutable> result = await harness.Identifier.IdentifyAsync(@"D:\nope\VPinballX.exe");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Identifying_a_folder_returns_every_executable_recognised_first()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller();
        var harness = new NudgeTestHarness(installation);

        IReadOnlyList<VpxExecutable> executables =
            await harness.Identifier.IdentifyFolderAsync(installation.RootPath);

        executables.Should().HaveCount(6, "five Visual Pinball executables plus the uninstaller");
        executables.Count(e => e.IsRecognised).Should().Be(5);
        executables[0].IsRecognised.Should().BeTrue("recognised builds are listed first");
        executables.Should().ContainSingle(e => e.FileName == "unins000.exe")
            .Which.Flavor.Should().Be(VpxFlavor.Unknown);
    }

    // -------------------------------------------------------------------------------------------

    private static async Task<VpxExecutable> IdentifyAsync(
        string fileName,
        string? version,
        ProcessorArchitecture architecture = ProcessorArchitecture.X64)
    {
        var fileSystem = new MockFileSystem();
        string path = $@"D:\vpx\{fileName}";
        fileSystem.AddFile(path, new MockFileData(SyntheticPortableExecutable.Build(architecture)));

        var harness = new NudgeTestHarness(fileSystem);
        if (version is not null)
        {
            harness.VersionInfo.SetVisualPinball(path, version);
        }

        Result<VpxExecutable> result = await harness.Identifier.IdentifyAsync(path);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }
}
