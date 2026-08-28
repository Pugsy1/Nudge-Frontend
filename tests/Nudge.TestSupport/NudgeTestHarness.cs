using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Vpx.Discovery;
using Nudge.Vpx.Identification;
using Nudge.Vpx.Platform;
using Nudge.Vpx.Settings;

namespace Nudge.TestSupport;

/// <summary>
/// Wires up the real Nudge components against an in-memory disk.
///
/// Everything here is the production class, not a stub, apart from the three things that genuinely
/// cannot run against a fake filesystem: the version resource reader, the registry, and the
/// environment. That means a passing test is evidence about the real code path, not about a mock.
/// </summary>
public sealed class NudgeTestHarness
{
    public NudgeTestHarness(SyntheticInstallation installation)
        : this(installation.FileSystem, installation.VersionInfo, installation.Environment)
    {
        RootPath = installation.RootPath;
    }

    public NudgeTestHarness(
        MockFileSystem fileSystem,
        FakeFileVersionInfoReader? versionInfo = null,
        FakeEnvironmentPaths? environment = null)
    {
        FileSystem = fileSystem;
        VersionInfo = versionInfo ?? new FakeFileVersionInfoReader();
        Environment = environment ?? new FakeEnvironmentPaths();
        Registry = new FakeRegistryReader();
        Redactor = new PathRedactor(Environment.UserName, Environment.UserProfile);

        ArchitectureReader = new PeArchitectureReader(
            FileSystem,
            Redactor,
            NullLogger<PeArchitectureReader>.Instance);

        Identifier = new VpxExecutableIdentifier(
            FileSystem,
            ArchitectureReader,
            VersionInfo,
            Redactor,
            NullLogger<VpxExecutableIdentifier>.Instance);

        Validator = new InstallationValidator(
            FileSystem,
            Identifier,
            Environment,
            Redactor,
            NullLogger<InstallationValidator>.Instance);
    }

    public MockFileSystem FileSystem { get; }

    public FakeFileVersionInfoReader VersionInfo { get; }

    public FakeEnvironmentPaths Environment { get; }

    public FakeRegistryReader Registry { get; }

    public IPathRedactor Redactor { get; }

    public PeArchitectureReader ArchitectureReader { get; }

    public IVpxExecutableIdentifier Identifier { get; }

    public InstallationValidator Validator { get; }

    /// <summary>The installation root, when this harness was built from a synthetic layout.</summary>
    public string RootPath { get; } = string.Empty;

    public RegistryCandidateProvider RegistryProvider => new(
        Registry,
        FileSystem,
        Redactor,
        NullLogger<RegistryCandidateProvider>.Instance);

    public KnownPathCandidateProvider KnownPathProvider => new(
        FileSystem,
        Environment,
        NullLogger<KnownPathCandidateProvider>.Instance);

    public SettingsFileCandidateProvider SettingsFileProvider => new(
        FileSystem,
        Environment,
        Redactor,
        NullLogger<SettingsFileCandidateProvider>.Instance);

    /// <summary>
    /// Discovery wired with whichever layers a test wants. Passing none uses all three, which is
    /// what the application does.
    /// </summary>
    public IVpxInstallationDiscovery BuildDiscovery(params IInstallationCandidateProvider[] providers)
    {
        IInstallationCandidateProvider[] layers = providers.Length > 0
            ? providers
            : [RegistryProvider, KnownPathProvider, SettingsFileProvider];

        return new VpxInstallationDiscovery(
            layers,
            Validator,
            FileSystem,
            Redactor,
            NullLogger<VpxInstallationDiscovery>.Instance);
    }

    public ISettingsService BuildSettingsService(string? settingsPath = null) => new JsonSettingsService(
        FileSystem,
        settingsPath ?? FileSystem.Path.Combine(Environment.LocalAppData, "Nudge", "settings.json"),
        Redactor,
        NullLogger<JsonSettingsService>.Instance);
}
