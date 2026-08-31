using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Vpx.Controller;
using Nudge.Vpx.Discovery;
using Nudge.Vpx.Identification;
using Nudge.Vpx.Launching;
using Nudge.Vpx.Platform;
using Nudge.Vpx.Roms;
using Nudge.Vpx.Settings;
using Nudge.Vpx.TableFiles;
using Nudge.Vpx.Windowing;

namespace Nudge.Vpx.DependencyInjection;

/// <summary>
/// Registers everything Nudge.Vpx provides. The application composes services here rather than
/// reaching for them, so nothing in the UI ever constructs a filesystem or a registry reader.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Folder under %LocalAppData% that holds Nudge's settings and logs.</summary>
    public const string NudgeDataFolderName = "Nudge";

    public const string SettingsFileName = "settings.json";

    public const string LogsFolderName = "logs";

    public static IServiceCollection AddNudgeVpx(this IServiceCollection services)
    {
        // Every filesystem call in Nudge goes through this, so tests can swap in an in-memory disk.
        services.TryAddSingleton<IFileSystem, FileSystem>();

        services.TryAddSingleton<IEnvironmentPaths, WindowsEnvironmentPaths>();
        services.TryAddSingleton<IRegistryReader, WindowsRegistryReader>();

        services.TryAddSingleton<IPathRedactor>(provider =>
        {
            IEnvironmentPaths environment = provider.GetRequiredService<IEnvironmentPaths>();
            return new PathRedactor(environment.UserName, environment.UserProfile);
        });

        services.TryAddSingleton<IPeArchitectureReader, PeArchitectureReader>();
        services.TryAddSingleton<IFileVersionInfoReader, FileVersionInfoReader>();
        services.TryAddSingleton<IVpxExecutableIdentifier, VpxExecutableIdentifier>();

        // Discovery layers. Registered as a collection; the orchestrator sorts them by Order.
        services.AddSingleton<IInstallationCandidateProvider, RegistryCandidateProvider>();
        services.AddSingleton<IInstallationCandidateProvider, KnownPathCandidateProvider>();
        services.AddSingleton<IInstallationCandidateProvider, SettingsFileCandidateProvider>();

        services.TryAddSingleton<InstallationValidator>();
        services.TryAddSingleton<IVpxInstallationDiscovery, VpxInstallationDiscovery>();

        services.TryAddSingleton<IOleTableInfoReader, OleTableInfoReader>();
        services.TryAddSingleton<ITableVideoLocator, Media.TableVideoLocator>();
        services.TryAddSingleton<ITableFilenameParser, TableFilenameParser>();
        services.TryAddSingleton<ITableFileReader, VpxTableFileReader>();

        // Second-pass, deliberately not part of the fast scan - see AGENTS.md section 4.5 and the
        // remarks on IRomNameReader.
        services.TryAddSingleton<IGameDataScriptReader, GameDataScriptReader>();
        services.TryAddSingleton<IRomNameParser, RomNameParser>();
        services.TryAddSingleton<IRomNameReader, RomNameReader>();

        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<IControllerReader, XInputControllerReader>();
        services.TryAddSingleton<IKeyboardInputSynthesizer, SendInputKeyboardSynthesizer>();
        services.TryAddSingleton<IForegroundWindowService, WindowsForegroundWindowService>();
        services.TryAddSingleton<IControllerInputService, ControllerInputService>();
        services.TryAddSingleton<IWindowSnapshotProvider, Win32WindowSnapshotProvider>();
        services.TryAddSingleton<IWindowActivator, Win32WindowActivator>();
        services.TryAddSingleton<IProcessLivenessChecker, ProcessLivenessChecker>();
        services.TryAddSingleton<ITableWindowWatcher, TableWindowWatcher>();
        services.TryAddSingleton<ILaunchEngine, LaunchEngine>();

        // Standalone building block for the health system (Phase 7) - not wired into anything yet.
        services.TryAddSingleton<IRomAvailabilityChecker, RomAvailabilityChecker>();

        services.TryAddSingleton<ISettingsService>(provider => new JsonSettingsService(
            provider.GetRequiredService<IFileSystem>(),
            ResolveSettingsFilePath(provider.GetRequiredService<IEnvironmentPaths>()),
            provider.GetRequiredService<IPathRedactor>(),
            provider.GetRequiredService<ILogger<JsonSettingsService>>()));

        return services;
    }

    /// <summary>%LocalAppData%\Nudge - Nudge's own data directory, never inside the VPX install.</summary>
    public static string ResolveDataDirectory(IEnvironmentPaths environment) =>
        Path.Combine(environment.LocalAppData, NudgeDataFolderName);

    public static string ResolveSettingsFilePath(IEnvironmentPaths environment) =>
        Path.Combine(ResolveDataDirectory(environment), SettingsFileName);

    public static string ResolveLogsDirectory(IEnvironmentPaths environment) =>
        Path.Combine(ResolveDataDirectory(environment), LogsFolderName);
}
