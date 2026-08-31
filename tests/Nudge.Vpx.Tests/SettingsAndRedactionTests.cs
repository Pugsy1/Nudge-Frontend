using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.TestSupport;
using Nudge.Vpx.Settings;
using Xunit;

namespace Nudge.Vpx.Tests;

public sealed class JsonSettingsServiceTests
{
    private const string SettingsPath = @"C:\Users\TestUser\AppData\Local\Nudge\settings.json";

    [Fact]
    public async Task Settings_survive_a_save_and_load_round_trip()
    {
        var harness = new NudgeTestHarness(new MockFileSystem());
        ISettingsService service = harness.BuildSettingsService(SettingsPath);

        var original = new NudgeSettings
        {
            SelectedInstallationId = "abc123def456",
            SelectedInstallationPath = @"D:\vPinball\VisualPinball",
            ThemeName = "Light",
            KnownInstallations =
            [
                new KnownInstallation
                {
                    Id = "abc123def456",
                    RootPath = @"D:\vPinball\VisualPinball",
                    DisplayName = "VisualPinball",
                    DateAdded = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero),
                    IsDefault = true
                }
            ]
        };

        await service.SaveAsync(original);
        NudgeSettings loaded = await service.LoadAsync();

        loaded.SelectedInstallationId.Should().Be(original.SelectedInstallationId);
        loaded.SelectedInstallationPath.Should().Be(original.SelectedInstallationPath);
        loaded.ThemeName.Should().Be("Light");
        loaded.KnownInstallations.Should().ContainSingle();
        loaded.KnownInstallations[0].RootPath.Should().Be(@"D:\vPinball\VisualPinball");
        loaded.KnownInstallations[0].IsDefault.Should().BeTrue();
        loaded.KnownInstallations[0].DateAdded.Should().Be(original.KnownInstallations[0].DateAdded);
    }

    [Fact]
    public async Task Play_history_survives_a_save_and_load_round_trip()
    {
        var harness = new NudgeTestHarness(new MockFileSystem());
        ISettingsService service = harness.BuildSettingsService(SettingsPath);

        var lastPlayed = new DateTimeOffset(2026, 8, 30, 21, 15, 0, TimeSpan.Zero);

        await service.SaveAsync(new NudgeSettings
        {
            TablePlayStats =
            {
                [@"D:\Tables\Attack From Mars.vpx"] = new TablePlayStats
                {
                    TimesPlayed = 7,
                    TotalPlaySeconds = 12_345,
                    LastPlayedAt = lastPlayed
                }
            }
        });

        NudgeSettings loaded = await service.LoadAsync();

        loaded.TablePlayStats.Should().ContainKey(@"D:\Tables\Attack From Mars.vpx");
        TablePlayStats stats = loaded.TablePlayStats[@"D:\Tables\Attack From Mars.vpx"];
        stats.TimesPlayed.Should().Be(7);
        stats.TotalPlaySeconds.Should().Be(12_345);
        stats.LastPlayedAt.Should().Be(lastPlayed);
    }

    /// <summary>
    /// Play history is the one thing in the settings file that cannot be recreated - a theme or a
    /// sort order is one click to set again, and hours played are gone for good. So it has to survive
    /// being read by a build that has never heard of it and written back out.
    /// </summary>
    [Fact]
    public async Task Play_history_is_not_lost_when_other_settings_are_saved()
    {
        var harness = new NudgeTestHarness(new MockFileSystem());
        ISettingsService service = harness.BuildSettingsService(SettingsPath);

        await service.SaveAsync(new NudgeSettings
        {
            TablePlayStats =
            {
                [@"D:\Tables\Medieval Madness.vpx"] = new TablePlayStats { TimesPlayed = 3, TotalPlaySeconds = 900 }
            }
        });

        // Something entirely unrelated changes - the shape every other preference save takes.
        await service.MutateAsync(s => s.ThemeName = "Light");

        NudgeSettings loaded = await service.LoadAsync();

        loaded.ThemeName.Should().Be("Light");
        loaded.TablePlayStats[@"D:\Tables\Medieval Madness.vpx"].TimesPlayed.Should().Be(3);
        loaded.TablePlayStats[@"D:\Tables\Medieval Madness.vpx"].TotalPlaySeconds.Should().Be(900);
    }

    /// <summary>A settings file written before play tracking existed must still load.</summary>
    [Fact]
    public async Task A_settings_file_without_play_history_loads_with_an_empty_one()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(SettingsPath, new MockFileData("""{ "SettingsVersion": 1, "ThemeName": "Dark" }"""));

        var harness = new NudgeTestHarness(fileSystem);
        ISettingsService service = harness.BuildSettingsService(SettingsPath);

        NudgeSettings loaded = await service.LoadAsync();

        loaded.TablePlayStats.Should().NotBeNull();
        loaded.TablePlayStats.Should().BeEmpty();
    }

    [Fact]
    public async Task Loading_when_no_settings_file_exists_returns_usable_defaults()
    {
        var harness = new NudgeTestHarness(new MockFileSystem());

        NudgeSettings settings = await harness.BuildSettingsService(SettingsPath).LoadAsync();

        settings.Should().NotBeNull();
        settings.SelectedInstallationId.Should().BeNull();
        settings.ThemeName.Should().Be("Dark");
        settings.KnownInstallations.Should().BeEmpty();
    }

    /// <summary>
    /// A corrupt settings file must never stop Nudge starting, and must never be silently deleted or
    /// "repaired" - the user's file is left exactly as it is so it can be inspected.
    /// </summary>
    [Fact]
    public async Task A_corrupt_settings_file_falls_back_to_defaults_and_is_left_on_disk()
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(SettingsPath, new MockFileData("{ this is not valid json"));
        var harness = new NudgeTestHarness(fileSystem);

        NudgeSettings settings = await harness.BuildSettingsService(SettingsPath).LoadAsync();

        settings.SelectedInstallationId.Should().BeNull();
        fileSystem.File.Exists(SettingsPath).Should().BeTrue("the user's file must not be destroyed");
        fileSystem.File.ReadAllText(SettingsPath).Should().Be("{ this is not valid json");
    }

    [Fact]
    public async Task Saving_creates_the_data_directory_when_it_does_not_exist_yet()
    {
        var fileSystem = new MockFileSystem();
        var harness = new NudgeTestHarness(fileSystem);

        await harness.BuildSettingsService(SettingsPath).SaveAsync(new NudgeSettings());

        fileSystem.File.Exists(SettingsPath).Should().BeTrue();
    }

    [Fact]
    public async Task Saving_twice_overwrites_rather_than_appending()
    {
        var fileSystem = new MockFileSystem();
        var harness = new NudgeTestHarness(fileSystem);
        ISettingsService service = harness.BuildSettingsService(SettingsPath);

        await service.SaveAsync(new NudgeSettings { ThemeName = "Dark" });
        await service.SaveAsync(new NudgeSettings { ThemeName = "Light" });

        NudgeSettings loaded = await service.LoadAsync();
        loaded.ThemeName.Should().Be("Light");
    }

    [Fact]
    public async Task Saving_leaves_no_temporary_file_behind()
    {
        var fileSystem = new MockFileSystem();
        var harness = new NudgeTestHarness(fileSystem);

        await harness.BuildSettingsService(SettingsPath).SaveAsync(new NudgeSettings());

        fileSystem.File.Exists(SettingsPath + ".tmp").Should().BeFalse();
    }
}

/// <summary>
/// Logs carry full paths on purpose, but the Windows username must never reach disk: users paste
/// logs into public forums.
/// </summary>
public sealed class PathRedactorTests
{
    [Fact]
    public void Removes_the_username_from_a_profile_path()
    {
        var redactor = new PathRedactor("Orion", @"C:\Users\Orion");

        string result = redactor.Redact(@"C:\Users\Orion\AppData\Local\Nudge\settings.json");

        result.Should().NotContain("Orion");
        result.Should().Contain("<user>");
        result.Should().Contain("AppData", "the rest of the path is still useful for diagnosis");
    }

    [Fact]
    public void Removes_other_account_names_that_appear_under_Users()
    {
        var redactor = new PathRedactor("Orion", @"C:\Users\Orion");

        string result = redactor.Redact(@"C:\Users\SomebodyElse\Desktop\table.vpx");

        result.Should().NotContain("SomebodyElse");
    }

    [Fact]
    public void Removes_a_bare_username_that_is_not_part_of_a_path()
    {
        var redactor = new PathRedactor("Orion", @"C:\Users\Orion");

        redactor.Redact("Discovery started for user Orion").Should().NotContain("Orion");
    }

    [Fact]
    public void Leaves_paths_that_contain_no_username_untouched()
    {
        var redactor = new PathRedactor("Orion", @"C:\Users\Orion");

        const string path = @"D:\vPinball\VisualPinball\VPinballX_GL64.exe";
        redactor.Redact(path).Should().Be(path);
    }

    /// <summary>
    /// A very short username would match inside ordinary words and turn logs into nonsense. The
    /// path-anchored rule still covers it, so nothing is lost.
    /// </summary>
    [Fact]
    public void A_very_short_username_is_not_redacted_on_its_own_but_is_still_redacted_in_a_path()
    {
        var redactor = new PathRedactor("Al", @"C:\Users\Al");

        redactor.Redact("Already scanned 5 folders").Should().Contain("Already");
        redactor.Redact(@"C:\Users\Al\Desktop").Should().NotContain(@"\Al\");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Handles_null_and_empty_input(string? input)
    {
        var redactor = new PathRedactor("Orion", @"C:\Users\Orion");

        redactor.Redact(input).Should().BeEmpty();
    }

    [Fact]
    public void Copes_with_an_unknown_username()
    {
        var redactor = new PathRedactor(userName: null);

        redactor.Redact(@"C:\Users\Orion\file.txt").Should().NotContain("Orion");
    }
}

public sealed class VpxIniFileTests
{
    [Fact]
    public void Parses_sections_and_values()
    {
        VpxIniFile ini = VpxIniFile.Parse(
        [
            "; a comment",
            "[Player]",
            "TablesDirectory = D:\\vPinball\\Tables",
            "FullScreen = 1",
            "",
            "[Standalone]",
            "MusicDirectory=D:\\vPinball\\Music"
        ]);

        ini.GetValue("Player", "TablesDirectory").Should().Be(@"D:\vPinball\Tables");
        ini.GetValue("Standalone", "MusicDirectory").Should().Be(@"D:\vPinball\Music");
    }

    [Fact]
    public void Finds_a_key_regardless_of_which_section_holds_it()
    {
        VpxIniFile ini = VpxIniFile.Parse(["[SomethingUnexpected]", "TablesDirectory = D:\\Tables"]);

        ini.FindValue("TablesDirectory").Should().Be(@"D:\Tables");
    }

    [Fact]
    public void Keys_and_sections_are_case_insensitive()
    {
        VpxIniFile ini = VpxIniFile.Parse(["[player]", "tablesdirectory = D:\\Tables"]);

        ini.GetValue("Player", "TablesDirectory").Should().Be(@"D:\Tables");
    }

    [Fact]
    public void Strips_surrounding_quotes_from_values()
    {
        VpxIniFile ini = VpxIniFile.Parse(["[Player]", "TablesDirectory = \"D:\\My Tables\""]);

        ini.FindValue("TablesDirectory").Should().Be(@"D:\My Tables");
    }

    [Fact]
    public void Ignores_comments_and_blank_and_malformed_lines()
    {
        VpxIniFile ini = VpxIniFile.Parse(
        [
            "# hash comment",
            "; semicolon comment",
            "",
            "   ",
            "no equals sign here",
            "=novalue",
            "[Player]",
            "TablesDirectory = D:\\Tables"
        ]);

        ini.FindValue("TablesDirectory").Should().Be(@"D:\Tables");
    }

    [Fact]
    public void An_absent_key_returns_null_rather_than_throwing()
    {
        VpxIniFile ini = VpxIniFile.Parse(["[Player]", "FullScreen = 1"]);

        ini.FindValue("TablesDirectory").Should().BeNull();
        ini.GetValue("NoSuchSection", "NoSuchKey").Should().BeNull();
    }

    [Fact]
    public async Task Reading_a_missing_file_returns_an_empty_result_rather_than_failing()
    {
        var fileSystem = new MockFileSystem();

        VpxIniFile ini = await VpxIniFile.ReadAsync(fileSystem, @"D:\nope\VPinballX.ini");

        ini.FindValue("TablesDirectory").Should().BeNull();
    }
}
