using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.TestSupport;
using Nudge.Vpx.Platform;
using Nudge.Vpx.Roms;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// <see cref="RomAvailabilityChecker"/> is a standalone building block for the health system
/// (Phase 7) - not wired into the scanner or the UI yet. It answers one question: does VPinMAME's
/// configured ROM folder have a zip for this ROM name.
/// </summary>
public sealed class RomAvailabilityCheckerTests
{
    private const string RomPath = @"D:\VPX\VPinMAME\roms";
    private const string VPinMameGlobalsKey = @"Software\Freeware\Visual PinMame\globals";

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeRegistryReader _registry = new();

    [Fact]
    public async Task Reports_Found_when_the_ROM_zip_exists_in_the_registered_rompath()
    {
        _registry.SetValue(RegistryHiveKind.CurrentUser, VPinMameGlobalsKey, "rompath", RomPath);
        _fileSystem.AddFile(_fileSystem.Path.Combine(RomPath, "mm_109b.zip"), new MockFileData([1]));

        RomAvailability result = await CreateChecker().CheckAsync("mm_109b");

        result.Status.Should().Be(RomAvailabilityStatus.Found);
        result.CheckedPath.Should().Be(_fileSystem.Path.Combine(RomPath, "mm_109b.zip"));
    }

    [Fact]
    public async Task Reports_Missing_when_the_rompath_is_known_but_has_no_matching_zip()
    {
        _registry.SetValue(RegistryHiveKind.CurrentUser, VPinMameGlobalsKey, "rompath", RomPath);
        _fileSystem.AddDirectory(RomPath);

        RomAvailability result = await CreateChecker().CheckAsync("nonexistent_rom");

        result.Status.Should().Be(RomAvailabilityStatus.Missing);
    }

    [Fact]
    public async Task Reports_Unknown_rather_than_failing_when_rompath_is_not_registered_at_all()
    {
        RomAvailability result = await CreateChecker().CheckAsync("mm_109b");

        result.Status.Should().Be(RomAvailabilityStatus.Unknown);
        result.CheckedPath.Should().BeNull();
    }

    [Fact]
    public async Task Falls_back_to_LocalMachine_when_CurrentUser_has_no_rompath()
    {
        _registry.SetValue(RegistryHiveKind.LocalMachine, VPinMameGlobalsKey, "rompath", RomPath);
        _fileSystem.AddFile(_fileSystem.Path.Combine(RomPath, "bk2k_l4.zip"), new MockFileData([1]));

        RomAvailability result = await CreateChecker().CheckAsync("bk2k_l4");

        result.Status.Should().Be(RomAvailabilityStatus.Found);
    }

    [Fact]
    public async Task The_result_always_carries_the_ROM_name_that_was_asked_about()
    {
        RomAvailability result = await CreateChecker().CheckAsync("some_rom");

        result.RomName.Should().Be("some_rom");
    }

    private RomAvailabilityChecker CreateChecker() => new(
        _registry,
        _fileSystem,
        new PathRedactor("TestUser"),
        NullLogger<RomAvailabilityChecker>.Instance);
}
