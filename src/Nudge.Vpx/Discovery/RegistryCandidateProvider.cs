using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Vpx.Platform;

namespace Nudge.Vpx.Discovery;

/// <summary>
/// Layer 1: what the registry already knows.
///
/// This is the most reliable layer because the paths were written by installers rather than guessed
/// by Nudge. Two things are consulted:
///
/// 1. COM registrations. Visual Pinball, VPinMAME and the B2S backglass server all register COM
///    servers whose recorded path sits inside, or next to, a Visual Pinball tree.
/// 2. VPinMAME's rompath, which points at the ROM folder. That folder is conventionally a few
///    levels below the installation root, so its ancestors are offered as candidates.
///
/// Neither gives the installation root directly, so everything here is still only a candidate.
/// </summary>
public sealed class RegistryCandidateProvider : IInstallationCandidateProvider
{
    /// <summary>
    /// VPinMAME's own settings key. All Visual Pinball installations on a machine share one
    /// VPinMAME registration, so this points at whichever install registered last.
    /// </summary>
    private const string VPinMameGlobalsKey = @"Software\Freeware\Visual PinMame\globals";

    /// <summary>
    /// COM ProgIDs worth asking about. Looked up by name rather than by hard-coded CLSID, because a
    /// hard-coded GUID would silently stop working if a future release re-registered under a new one.
    /// </summary>
    private static readonly string[] ProgIds =
    [
        "VPinball.Table",
        "VPinballX.Table",
        "VPinball.Controller",
        "VPinballX.Controller",
        "VPinMAME.Controller",
        "B2S.Server",
        "B2S.SBServer"
    ];

    /// <summary>
    /// How far above the ROM folder to look for the installation root. A Baller layout puts ROMs at
    /// &lt;root&gt;\VPinMAME\roms, so two levels is the usual answer; three covers deeper layouts.
    /// </summary>
    private const int RomPathAncestorLevels = 3;

    private readonly IRegistryReader _registry;
    private readonly IFileSystem _fileSystem;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<RegistryCandidateProvider> _logger;

    public RegistryCandidateProvider(
        IRegistryReader registry,
        IFileSystem fileSystem,
        IPathRedactor redactor,
        ILogger<RegistryCandidateProvider> logger)
    {
        _registry = registry;
        _fileSystem = fileSystem;
        _redactor = redactor;
        _logger = logger;
    }

    public int Order => 1;

    public string Name => "Registry";

    public Task<IReadOnlyList<InstallationCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<InstallationCandidate>();

        try
        {
            AddComServerCandidates(candidates);
            AddRomPathCandidates(candidates);
            AddAppPathsCandidates(candidates);
        }
        catch (Exception ex)
        {
            // A discovery layer must never take startup down with it.
            _logger.LogWarning(ex, "The registry discovery layer failed and contributed nothing.");
        }

        return Task.FromResult<IReadOnlyList<InstallationCandidate>>(candidates);
    }

    private void AddComServerCandidates(List<InstallationCandidate> candidates)
    {
        foreach (string progId in ProgIds)
        {
            string? clsid = _registry.ReadString(RegistryHiveKind.ClassesRoot, $@"{progId}\CLSID", valueName: null);
            if (string.IsNullOrWhiteSpace(clsid))
            {
                continue;
            }

            foreach (string serverKind in (string[])["LocalServer32", "InprocServer32"])
            {
                string? serverPath = _registry.ReadString(
                    RegistryHiveKind.ClassesRoot,
                    $@"CLSID\{clsid}\{serverKind}",
                    valueName: null);

                if (string.IsNullOrWhiteSpace(serverPath))
                {
                    continue;
                }

                string? folder = DirectoryOfRegisteredServer(serverPath);
                if (folder is null)
                {
                    continue;
                }

                AddCandidate(
                    candidates,
                    folder,
                    $"The COM component '{progId}' is registered at this location.");

                // VPinMAME and B2S register inside a subfolder of the installation, or inside the
                // Tables folder, so the parent is worth probing too.
                string? parent = TryGetParent(folder);
                if (parent is not null)
                {
                    AddCandidate(
                        candidates,
                        parent,
                        $"This is the folder above the registered COM component '{progId}'.");
                }
            }
        }
    }

    private void AddRomPathCandidates(List<InstallationCandidate> candidates)
    {
        string? romPath =
            _registry.ReadString(RegistryHiveKind.CurrentUser, VPinMameGlobalsKey, "rompath")
            ?? _registry.ReadString(RegistryHiveKind.LocalMachine, VPinMameGlobalsKey, "rompath");

        if (string.IsNullOrWhiteSpace(romPath))
        {
            return;
        }

        _logger.LogDebug("VPinMAME reports its ROM path as {RomPath}", _redactor.Redact(romPath));

        string? current = romPath;
        for (int level = 1; level <= RomPathAncestorLevels; level++)
        {
            current = TryGetParent(current);
            if (current is null)
            {
                break;
            }

            AddCandidate(
                candidates,
                current,
                $"VPinMAME's ROM folder is {level} level{(level == 1 ? string.Empty : "s")} below this "
                + "folder, and VPinMAME normally lives inside a Visual Pinball installation.");
        }
    }

    private void AddAppPathsCandidates(List<InstallationCandidate> candidates)
    {
        foreach (string exeName in (string[])["VPinballX.exe", "VPinballX_GL64.exe", "VPinballX_BGFX.exe"])
        {
            string? registered = _registry.ReadString(
                RegistryHiveKind.LocalMachine,
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}",
                valueName: null);

            if (string.IsNullOrWhiteSpace(registered))
            {
                continue;
            }

            string? folder = DirectoryOfRegisteredServer(registered);
            if (folder is not null)
            {
                AddCandidate(candidates, folder, $"Windows records an application path for {exeName} here.");
            }
        }
    }

    /// <summary>
    /// Turns a registered server command line into the folder that holds it. Registered paths are
    /// often quoted and often carry arguments such as /automation.
    /// </summary>
    private string? DirectoryOfRegisteredServer(string registeredValue)
    {
        string value = registeredValue.Trim();

        if (value.StartsWith('"'))
        {
            int closing = value.IndexOf('"', 1);
            value = closing > 1 ? value[1..closing] : value.Trim('"');
        }
        else
        {
            // Unquoted values may still have arguments after the .exe or .dll.
            int extension = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (extension < 0)
            {
                extension = value.IndexOf(".dll", StringComparison.OrdinalIgnoreCase);
            }

            if (extension > 0)
            {
                value = value[..(extension + 4)];
            }
        }

        try
        {
            string? directory = _fileSystem.Path.GetDirectoryName(value);
            return string.IsNullOrWhiteSpace(directory) ? null : directory;
        }
        catch (ArgumentException)
        {
            // A malformed path in somebody else's registry entry is not our problem to fix.
            return null;
        }
    }

    private string? TryGetParent(string path)
    {
        try
        {
            IDirectoryInfo? parent = _fileSystem.DirectoryInfo.New(path).Parent;
            return parent?.FullName;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    private static void AddCandidate(List<InstallationCandidate> candidates, string path, string reason)
    {
        if (!candidates.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(new InstallationCandidate(path, InstallationSource.Registry, reason));
        }
    }
}
