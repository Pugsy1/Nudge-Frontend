using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Nudge.TestSupport;
using Nudge.Vpx.Platform;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// Discovery across the layered strategy. Each layer is tested on its own so a failure points at
/// one thing, and then together so their merging is covered.
/// </summary>
public sealed class VpxInstallationDiscoveryTests
{
    private const string BallerRoot = @"D:\vPinball\VisualPinball";

    // -------------------------------------------------------------------------------------------
    // Layer 1: registry
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Finds_an_installation_from_the_VPinMAME_rom_path()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller(BallerRoot);
        var harness = new NudgeTestHarness(installation);

        // VPinMAME records where its ROMs live. That folder sits two levels below the install root.
        harness.Registry.SetValue(
            RegistryHiveKind.CurrentUser,
            @"Software\Freeware\Visual PinMame\globals",
            "rompath",
            $@"{BallerRoot}\VPinMAME\roms");

        IVpxInstallationDiscovery discovery = harness.BuildDiscovery(harness.RegistryProvider);
        IReadOnlyList<VpxInstallation> found = await discovery.DiscoverAsync();

        found.Should().ContainSingle();
        found[0].RootPath.Should().Be(BallerRoot);
        found[0].DiscoverySource.Should().Be(InstallationSource.Registry);
    }

    [Fact]
    public async Task Finds_an_installation_from_a_registered_COM_server()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller(BallerRoot);
        var harness = new NudgeTestHarness(installation);

        harness.Registry.SetComServer(
            "VPinball.Table",
            "{00000000-1111-2222-3333-444444444444}",
            $@"""{BallerRoot}\VPinballX.exe"" /automation");

        IVpxInstallationDiscovery discovery = harness.BuildDiscovery(harness.RegistryProvider);
        IReadOnlyList<VpxInstallation> found = await discovery.DiscoverAsync();

        found.Should().ContainSingle().Which.RootPath.Should().Be(BallerRoot);
    }

    [Fact]
    public async Task An_empty_registry_produces_no_installations_rather_than_failing()
    {
        var harness = new NudgeTestHarness(InstallationLayouts.Baller(BallerRoot));

        IVpxInstallationDiscovery discovery = harness.BuildDiscovery(harness.RegistryProvider);
        IReadOnlyList<VpxInstallation> found = await discovery.DiscoverAsync();

        found.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------------------------
    // Layer 2: known paths
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Finds_an_installation_at_a_conventional_path_on_a_non_system_drive()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller(BallerRoot);
        var harness = new NudgeTestHarness(installation);

        IVpxInstallationDiscovery discovery = harness.BuildDiscovery(harness.KnownPathProvider);
        IReadOnlyList<VpxInstallation> found = await discovery.DiscoverAsync();

        found.Should().ContainSingle(i => i.RootPath.Equals(BallerRoot, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Does_not_invent_an_installation_where_none_exists()
    {
        var harness = new NudgeTestHarness(InstallationLayouts.Empty());

        IVpxInstallationDiscovery discovery = harness.BuildDiscovery(harness.KnownPathProvider);
        IReadOnlyList<VpxInstallation> found = await discovery.DiscoverAsync();

        found.Should().BeEmpty();
    }

    [Fact]
    public async Task A_folder_full_of_unrelated_programs_is_not_reported_as_an_installation()
    {
        SyntheticInstallation installation = InstallationLayouts.Ambiguous(@"D:\Visual Pinball");
        var harness = new NudgeTestHarness(installation);

        IVpxInstallationDiscovery discovery = harness.BuildDiscovery(harness.KnownPathProvider);
        IReadOnlyList<VpxInstallation> found = await discovery.DiscoverAsync();

        found.Should().BeEmpty("the folder name matches a convention but the contents are not Visual Pinball");
    }

    // -------------------------------------------------------------------------------------------
    // Layer 3: the Visual Pinball settings file
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Finds_an_installation_from_a_TablesDirectory_hint_in_VPinballX_ini()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller(BallerRoot);
        var harness = new NudgeTestHarness(installation);

        string iniPath = harness.FileSystem.Path.Combine(
            harness.Environment.RoamingAppData, "VPinballX", "VPinballX.ini");

        harness.FileSystem.AddFile(iniPath, new MockFileData(
            $"[Player]{Environment.NewLine}TablesDirectory = {BallerRoot}\\Tables{Environment.NewLine}"));

        IVpxInstallationDiscovery discovery = harness.BuildDiscovery(harness.SettingsFileProvider);
        IReadOnlyList<VpxInstallation> found = await discovery.DiscoverAsync();

        found.Should().ContainSingle().Which.RootPath.Should().Be(BallerRoot);
        found[0].DiscoverySource.Should().Be(InstallationSource.SettingsFile);
    }

    [Fact]
    public async Task A_missing_VPinballX_ini_produces_no_candidates()
    {
        var harness = new NudgeTestHarness(InstallationLayouts.Baller(BallerRoot));

        IVpxInstallationDiscovery discovery = harness.BuildDiscovery(harness.SettingsFileProvider);
        IReadOnlyList<VpxInstallation> found = await discovery.DiscoverAsync();

        found.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------------------------
    // All layers together
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_same_folder_found_by_several_layers_is_reported_once_with_all_the_evidence()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller(BallerRoot);
        var harness = new NudgeTestHarness(installation);

        harness.Registry.SetValue(
            RegistryHiveKind.CurrentUser,
            @"Software\Freeware\Visual PinMame\globals",
            "rompath",
            $@"{BallerRoot}\VPinMAME\roms");

        string iniPath = harness.FileSystem.Path.Combine(
            harness.Environment.RoamingAppData, "VPinballX", "VPinballX.ini");
        harness.FileSystem.AddFile(iniPath, new MockFileData(
            $"[Player]{Environment.NewLine}TablesDirectory = {BallerRoot}\\Tables{Environment.NewLine}"));

        IVpxInstallationDiscovery discovery = harness.BuildDiscovery();
        IReadOnlyList<VpxInstallation> found = await discovery.DiscoverAsync();

        found.Should().ContainSingle("the same folder must not be listed three times");
        found[0].Evidence.Count.Should().BeGreaterThan(2, "each layer that agreed contributes evidence");
    }

    [Fact]
    public async Task The_best_installation_is_marked_as_the_default()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller(BallerRoot);
        var harness = new NudgeTestHarness(installation);

        // A second, weaker installation elsewhere on the same disk: no tables folder, no version
        // resource, so nothing corroborates its filename hint.
        const string minimalRoot = @"D:\VPX";
        harness.FileSystem.AddFile(
            $@"{minimalRoot}\VPinballX_GL64.exe",
            new MockFileData(SyntheticPortableExecutable.X64()));

        IVpxInstallationDiscovery discovery = harness.BuildDiscovery(harness.KnownPathProvider);
        IReadOnlyList<VpxInstallation> found = await discovery.DiscoverAsync();

        found.Should().HaveCountGreaterThan(1);
        found.Count(i => i.IsDefault).Should().Be(1);
        found[0].IsDefault.Should().BeTrue();
        found[0].RootPath.Should().Be(BallerRoot, "the complete installation is the better default");
    }

    // -------------------------------------------------------------------------------------------
    // Manual selection
    // -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_manually_chosen_installation_folder_is_accepted()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller(BallerRoot);
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result = await harness.BuildDiscovery().InspectFolderAsync(BallerRoot);

        result.IsSuccess.Should().BeTrue();
        result.Value.DiscoverySource.Should().Be(InstallationSource.Manual);
        result.Value.IsDefault.Should().BeTrue();
    }

    /// <summary>
    /// Users routinely pick the folder above the real one. Nudge looks one level down before giving
    /// up, rather than telling them they are wrong.
    /// </summary>
    [Fact]
    public async Task Choosing_the_folder_one_level_above_the_installation_still_works()
    {
        SyntheticInstallation installation = InstallationLayouts.Baller(BallerRoot);
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result = await harness.BuildDiscovery().InspectFolderAsync(@"D:\vPinball");

        result.IsSuccess.Should().BeTrue();
        result.Value.RootPath.Should().Be(BallerRoot);
    }

    [Fact]
    public async Task Choosing_a_folder_that_is_not_Visual_Pinball_fails_clearly_without_throwing()
    {
        SyntheticInstallation installation = InstallationLayouts.Ambiguous();
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result =
            await harness.BuildDiscovery().InspectFolderAsync(installation.RootPath);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Choosing_an_empty_folder_fails_clearly()
    {
        SyntheticInstallation installation = InstallationLayouts.Empty();
        var harness = new NudgeTestHarness(installation);

        Result<VpxInstallation> result =
            await harness.BuildDiscovery().InspectFolderAsync(installation.RootPath);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Choosing_nothing_fails_rather_than_throwing()
    {
        var harness = new NudgeTestHarness(new MockFileSystem());

        Result<VpxInstallation> result = await harness.BuildDiscovery().InspectFolderAsync("   ");

        result.IsFailure.Should().BeTrue();
    }
}
