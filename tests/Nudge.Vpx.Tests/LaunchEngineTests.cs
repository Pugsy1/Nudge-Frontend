using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Nudge.Vpx.Launching;
using Xunit;

namespace Nudge.Vpx.Tests;

/// <summary>
/// <see cref="LaunchEngine"/> is Phase 5's launch engine: pick the right executable, run "-Play
/// &lt;table&gt;", wait for Visual Pinball to exit. <see cref="IProcessRunner"/> is faked - a real
/// process is exactly the kind of I/O boundary Nudge's tests fake rather than mock (see
/// SESSIONHANDOFF2's testing-strategy note), the same role <c>MockFileSystem</c> plays elsewhere.
/// </summary>
public sealed class LaunchEngineTests
{
    // DirectX9, not OpenGL: the OpenGL build autodetects SteamVR and can launch straight into VR
    // with no flag involved (see VpxInstallation.BestDesktopExecutable's remarks), so it is never
    // chosen for a Desktop launch and would make a poor stand-in "happy path" executable here.
    private const string ExecutablePath = @"D:\VPX\VPinballX64.exe";
    private const string TablePath = @"D:\VPX\Tables\Medieval Madness.vpx";

    private readonly MockFileSystem _fileSystem = new();
    private readonly FakeProcessRunner _processRunner = new();

    public LaunchEngineTests()
    {
        _fileSystem.AddFile(TablePath, new MockFileData([1, 2, 3]));
    }

    [Fact]
    public async Task Launches_the_installations_best_desktop_executable_with_Play_and_the_table_path()
    {
        LaunchEngine engine = CreateEngine();
        VpxInstallation installation = BuildInstallation(ExecutablePath, VpxFlavor.DirectX9);
        _processRunner.NextExitCode = 0;

        Result<LaunchOutcome> result = await engine.LaunchAsync(installation, TablePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExecutablePath.Should().Be(ExecutablePath);
        result.Value.ExitCode.Should().Be(0);

        _processRunner.LastFileName.Should().Be(ExecutablePath);
        _processRunner.LastArguments.Should().Equal("-Play", TablePath);
        _processRunner.LastWorkingDirectory.Should().Be(@"D:\VPX");
    }

    [Fact]
    public async Task A_non_zero_exit_code_is_still_a_successful_launch()
    {
        // Visual Pinball ran and exited - whatever happened during play is the health system's
        // concern in a later phase, not a reason for LaunchAsync itself to report failure.
        LaunchEngine engine = CreateEngine();
        VpxInstallation installation = BuildInstallation(ExecutablePath, VpxFlavor.DirectX9);
        _processRunner.NextExitCode = 1;

        Result<LaunchOutcome> result = await engine.LaunchAsync(installation, TablePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task Fails_without_launching_anything_when_the_table_file_is_missing()
    {
        LaunchEngine engine = CreateEngine();
        VpxInstallation installation = BuildInstallation(ExecutablePath, VpxFlavor.DirectX9);

        Result<LaunchOutcome> result = await engine.LaunchAsync(installation, @"D:\VPX\Tables\Missing.vpx");

        result.IsFailure.Should().BeTrue();
        _processRunner.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Fails_without_launching_anything_when_no_desktop_build_is_available()
    {
        LaunchEngine engine = CreateEngine();
        VpxInstallation installation = BuildInstallation(ExecutablePath, VpxFlavor.VP9Legacy);

        Result<LaunchOutcome> result = await engine.LaunchAsync(installation, TablePath);

        result.IsFailure.Should().BeTrue();
        _processRunner.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task A_process_that_fails_to_start_is_reported_as_a_failure_not_an_exception()
    {
        LaunchEngine engine = CreateEngine();
        VpxInstallation installation = BuildInstallation(ExecutablePath, VpxFlavor.DirectX9);
        _processRunner.ThrowOnRun = new InvalidOperationException("did not start");

        Result<LaunchOutcome> result = await engine.LaunchAsync(installation, TablePath);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("VPinballX64.exe");
    }

    [Fact]
    public async Task Launching_a_specific_executable_directly_ignores_Desktop_selection_entirely()
    {
        // This is exactly how "Play in VR" is expected to launch (VpxInstallation.BestVrExecutable),
        // via the OpenGL build - which BestDesktopExecutable would normally never select. The
        // two-argument overload must launch whatever executable it is given, no re-ranking.
        LaunchEngine engine = CreateEngine();
        var vrExecutable = new VpxExecutable
        {
            Path = @"D:\VPX\VPinballX_GL64.exe",
            FileName = "VPinballX_GL64.exe",
            Flavor = VpxFlavor.OpenGL,
            Architecture = ProcessorArchitecture.X64,
            VrCapability = VrCapability.OpenVR,
            Confidence = Confidence.High,
            Evidence = DetectionEvidence.Empty()
        };
        _processRunner.NextExitCode = 0;

        Result<LaunchOutcome> result = await engine.LaunchAsync(vrExecutable, TablePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExecutablePath.Should().Be(vrExecutable.Path);
        _processRunner.LastFileName.Should().Be(vrExecutable.Path);
        _processRunner.LastArguments.Should().Equal("-Play", TablePath);
    }

    [Fact]
    public async Task The_installation_overload_delegates_to_BestDesktopExecutable()
    {
        LaunchEngine engine = CreateEngine();
        VpxInstallation installation = BuildInstallation(ExecutablePath, VpxFlavor.DirectX9);

        Result<LaunchOutcome> result = await engine.LaunchAsync(installation, TablePath);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExecutablePath.Should().Be(ExecutablePath);
    }

    [Fact]
    public async Task Launching_a_specific_executable_still_fails_when_the_table_file_is_missing()
    {
        LaunchEngine engine = CreateEngine();
        var executable = new VpxExecutable
        {
            Path = ExecutablePath,
            FileName = Path.GetFileName(ExecutablePath),
            Flavor = VpxFlavor.DirectX9,
            Architecture = ProcessorArchitecture.X64,
            VrCapability = VrCapability.Unknown,
            Confidence = Confidence.High,
            Evidence = DetectionEvidence.Empty()
        };

        Result<LaunchOutcome> result = await engine.LaunchAsync(executable, @"D:\VPX\Tables\Missing.vpx");

        result.IsFailure.Should().BeTrue();
        _processRunner.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Controller_translation_is_never_started_when_the_setting_is_off()
    {
        var controllerInput = new FakeControllerInputService();
        LaunchEngine engine = CreateEngine(enableControllerSupport: false, controllerInput);
        VpxInstallation installation = BuildInstallation(ExecutablePath, VpxFlavor.DirectX9);
        _processRunner.NextExitCode = 0;

        await engine.LaunchAsync(installation, TablePath);

        controllerInput.WasStarted.Should().BeFalse();
    }

    [Fact]
    public async Task Controller_translation_starts_for_this_executable_and_stops_once_it_exits()
    {
        var controllerInput = new FakeControllerInputService();
        LaunchEngine engine = CreateEngine(enableControllerSupport: true, controllerInput);
        VpxInstallation installation = BuildInstallation(ExecutablePath, VpxFlavor.DirectX9);
        _processRunner.NextExitCode = 0;

        await engine.LaunchAsync(installation, TablePath);

        controllerInput.WasStarted.Should().BeTrue();
        controllerInput.LastTargetProcessName.Should().Be("VPinballX64", "the extension is stripped to match Process.ProcessName");
        controllerInput.LastSession.Should().NotBeNull();
        controllerInput.LastSession!.WasDisposed.Should().BeTrue("the session must not outlive the play session");
    }

    private LaunchEngine CreateEngine(bool enableControllerSupport = false, FakeControllerInputService? controllerInput = null) => new(
        _processRunner,
        _fileSystem,
        new PathRedactor("TestUser"),
        controllerInput ?? new FakeControllerInputService(),
        new FakeSettingsService { EnableControllerSupport = enableControllerSupport },
        NullLogger<LaunchEngine>.Instance);

    private static VpxInstallation BuildInstallation(string executablePath, VpxFlavor flavor) => new()
    {
        Id = "install-1",
        DisplayName = "Visual Pinball",
        RootPath = @"D:\VPX",
        TablesPath = @"D:\VPX\Tables",
        DiscoverySource = InstallationSource.Manual,
        Confidence = Confidence.High,
        Evidence = DetectionEvidence.Empty(),
        Executables =
        [
            new VpxExecutable
            {
                Path = executablePath,
                FileName = Path.GetFileName(executablePath),
                Flavor = flavor,
                Architecture = ProcessorArchitecture.X64,
                VrCapability = VrCapability.Unknown,
                Confidence = Confidence.High,
                Evidence = DetectionEvidence.Empty()
            }
        ]
    };

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public int NextExitCode { get; set; }

        public Exception? ThrowOnRun { get; set; }

        public bool WasCalled { get; private set; }

        public string? LastFileName { get; private set; }

        public IReadOnlyList<string>? LastArguments { get; private set; }

        public string? LastWorkingDirectory { get; private set; }

        public Task<int> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastFileName = fileName;
            LastArguments = arguments;
            LastWorkingDirectory = workingDirectory;

            return ThrowOnRun is not null
                ? Task.FromException<int>(ThrowOnRun)
                : Task.FromResult(NextExitCode);
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public bool EnableControllerSupport { get; set; }

        public string SettingsFilePath => @"D:\Nudge\settings.json";

        public Task<NudgeSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NudgeSettings { EnableControllerSupport = EnableControllerSupport });

        public Task SaveAsync(NudgeSettings settings, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task MutateAsync(Action<NudgeSettings> mutate, CancellationToken cancellationToken = default)
        {
            mutate(new NudgeSettings { EnableControllerSupport = EnableControllerSupport });
            return Task.CompletedTask;
        }
    }

    private sealed class FakeControllerInputService : IControllerInputService
    {
        public bool WasStarted { get; private set; }

        public string? LastTargetProcessName { get; private set; }

        public FakeSession? LastSession { get; private set; }

        public IDisposable StartTranslating(string targetProcessName, ControllerMapping mapping)
        {
            WasStarted = true;
            LastTargetProcessName = targetProcessName;
            return LastSession = new FakeSession();
        }

        public sealed class FakeSession : IDisposable
        {
            public bool WasDisposed { get; private set; }

            public void Dispose() => WasDisposed = true;
        }
    }
}
