using FluentAssertions;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Nudge.TestSupport;
using Nudge.Vpx.Discovery;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// Validation decides whether a candidate folder is really a Visual Pinball installation. It is the
/// only place that turns a guess into an answer, so it is tested against every layout shape Nudge
/// is expected to meet.
/// </summary>
public sealed class InstallationValidatorTests
{
    [Fact]
    public async Task Accepts_a_Baller_installation_and_describes_every_executable()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller();
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result = await ValidateAsync(harness, installation.RootPath);

        result.IsSuccess.Should().BeTrue();

        VpxInstallation value = result.Value;
        value.RootPath.Should().Be(installation.RootPath);
        value.HasTablesFolder.Should().BeTrue();
        value.RecognisedExecutables.Should().HaveCount(5);
        value.Confidence.Should().Be(Confidence.High);
    }

    /// <summary>
    /// The maintainer's actual machine: Baller-installed 10.8.0 with no BGFX build. VR must be
    /// reported as OpenVR, because that is the SteamVR path the OpenGL build uses.
    /// </summary>
    [Fact]
    public async Task A_Baller_installation_reports_OpenVR_as_its_best_VR_capability()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller();
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result = await ValidateAsync(harness, installation.RootPath);

        result.Value.BestVrCapability.Should().Be(VrCapability.OpenVR);
    }

    [Fact]
    public async Task Accepts_a_portable_installation_and_honours_its_own_ini_for_the_tables_folder()
    {
        SyntheticInstallation installation = InstallationLayouts.Portable();
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result = await ValidateAsync(harness, installation.RootPath);

        result.IsSuccess.Should().BeTrue();
        result.Value.TablesPath.Should().Be(installation.PathTo("MyTables"),
            "a VPinballX.ini beside the executables describes this installation");
        result.Value.RecognisedExecutables.Should().ContainSingle()
            .Which.Flavor.Should().Be(VpxFlavor.Bgfx);
    }

    [Fact]
    public async Task A_portable_BGFX_installation_at_10_8_1_reports_OpenXR()
    {
        SyntheticInstallation installation = InstallationLayouts.Portable(version: "10.8.1.0");
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result = await ValidateAsync(harness, installation.RootPath);

        result.Value.BestVrCapability.Should().Be(VrCapability.OpenXR);
    }

    /// <summary>
    /// A fresh install has no tables in it. That must lower confidence, not cause rejection.
    /// </summary>
    [Fact]
    public async Task Accepts_a_minimal_installation_with_no_Tables_folder()
    {
        SyntheticInstallation installation = InstallationLayouts.Minimal();
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result = await ValidateAsync(harness, installation.RootPath);

        result.IsSuccess.Should().BeTrue();
        result.Value.HasTablesFolder.Should().BeFalse();
        result.Value.Confidence.Should().NotBe(Confidence.High,
            "a missing tables folder should reduce certainty");
    }

    [Fact]
    public async Task Rejects_a_folder_whose_executables_are_not_Visual_Pinball()
    {
        SyntheticInstallation installation = InstallationLayouts.Ambiguous();
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result = await ValidateAsync(harness, installation.RootPath);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("none of them is Visual Pinball");
    }

    [Fact]
    public async Task A_Tables_folder_on_its_own_is_not_enough_to_call_something_Visual_Pinball()
    {
        SyntheticInstallation installation = InstallationLayouts.Ambiguous();
        var harness = new NudgeTestHarness(installation);

        harness.FileSystem.Directory.Exists(installation.PathTo("Tables")).Should().BeTrue();

        Result<VpxInstallation> result = await ValidateAsync(harness, installation.RootPath);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Rejects_an_empty_folder_with_a_message_a_user_can_understand()
    {
        SyntheticInstallation installation = InstallationLayouts.Empty();
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result = await ValidateAsync(harness, installation.RootPath);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("no programs");
    }

    [Fact]
    public async Task Rejects_a_folder_that_does_not_exist()
    {
        var harness = new NudgeTestHarness(InstallationLayouts.Empty());

        Result<VpxInstallation> result = await ValidateAsync(harness, @"D:\does\not\exist");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("does not exist");
    }

    [Fact]
    public async Task The_installation_id_is_stable_across_runs_and_ignores_trailing_slashes_and_case()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller();
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> first = await ValidateAsync(harness, installation.RootPath);
        Result<VpxInstallation> second = await ValidateAsync(harness, installation.RootPath + @"\");
        Result<VpxInstallation> third = await ValidateAsync(harness, installation.RootPath.ToUpperInvariant());

        first.Value.Id.Should().Be(second.Value.Id);
        first.Value.Id.Should().Be(third.Value.Id);
        first.Value.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Different_installations_get_different_ids()
    {
        SyntheticInstallation baller = InstallationLayouts.Baller();
        SyntheticInstallation minimal = InstallationLayouts.Minimal();

        Result<VpxInstallation> first = await ValidateAsync(new NudgeTestHarness(baller), baller.RootPath);
        Result<VpxInstallation> second = await ValidateAsync(new NudgeTestHarness(minimal), minimal.RootPath);

        first.Value.Id.Should().NotBe(second.Value.Id);
    }

    [Fact]
    public async Task Validation_records_evidence_for_the_user_to_read()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller();
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result = await ValidateAsync(harness, installation.RootPath);

        result.Value.Evidence.Should().NotBeEmpty();
        result.Value.Evidence.Summary.Should().Contain("Tables");
    }

    private static Task<Result<VpxInstallation>> ValidateAsync(NudgeTestHarness harness, string path) =>
        harness.Validator.ValidateAsync(
            new InstallationCandidate(path, InstallationSource.Manual, "Test."));
}
