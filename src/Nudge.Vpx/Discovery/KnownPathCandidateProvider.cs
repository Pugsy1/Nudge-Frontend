using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Models;
using Nudge.Vpx.Platform;

namespace Nudge.Vpx.Discovery;

/// <summary>
/// Layer 2: conventional install locations, probed rather than assumed.
///
/// Visual Pinball has no single canonical install path. Users install it wherever they like, and the
/// popular all-in-one installers each have their own layout. Nudge therefore holds a list of shapes
/// it has seen, applies every one of them to every fixed drive, and keeps only the folders that
/// actually exist. A path that is not on disk never becomes a candidate.
/// </summary>
public sealed class KnownPathCandidateProvider : IInstallationCandidateProvider
{
    /// <summary>
    /// Folder names, relative to a drive root, worth probing. Applied to every fixed drive rather
    /// than only C:, because plenty of cabinets keep the collection on a second disk.
    /// </summary>
    private static readonly string[] RelativeLayouts =
    [
        "Visual Pinball",
        "VisualPinball",
        "vPinball",
        "VPX",
        @"vPinball\VisualPinball",
        @"vPinball\Visual Pinball",
        @"Games\Visual Pinball",
        @"Games\VisualPinball",
        @"Pinball\Visual Pinball",
        @"Pinball\VisualPinball",
        @"VPinball\VisualPinball"
    ];

    /// <summary>
    /// Drive types not worth probing. A disconnected network drive can block for tens of seconds,
    /// and optical or removable media are not where anyone installs Visual Pinball.
    /// </summary>
    private static readonly HashSet<DriveType> SkippedDriveTypes =
    [
        DriveType.Network,
        DriveType.Removable,
        DriveType.CDRom,
        DriveType.NoRootDirectory
    ];

    private readonly IFileSystem _fileSystem;
    private readonly IEnvironmentPaths _environment;
    private readonly ILogger<KnownPathCandidateProvider> _logger;

    public KnownPathCandidateProvider(
        IFileSystem fileSystem,
        IEnvironmentPaths environment,
        ILogger<KnownPathCandidateProvider> logger)
    {
        _fileSystem = fileSystem;
        _environment = environment;
        _logger = logger;
    }

    public int Order => 2;

    public string Name => "Known paths";

    public Task<IReadOnlyList<InstallationCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<InstallationCandidate>();

        try
        {
            foreach (string driveRoot in GetFixedDriveRoots())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The drive root itself, for the rare portable install unpacked straight onto a disk.
                AddIfExists(candidates, driveRoot, $"Probed the root of drive {driveRoot}.");

                foreach (string layout in RelativeLayouts)
                {
                    string path = _fileSystem.Path.Combine(driveRoot, layout);
                    AddIfExists(candidates, path, $"'{layout}' is a conventional Visual Pinball folder name.");
                }
            }

            foreach (string? programFiles in (string?[])[_environment.ProgramFiles, _environment.ProgramFilesX86])
            {
                if (string.IsNullOrWhiteSpace(programFiles))
                {
                    continue;
                }

                foreach (string layout in (string[])["Visual Pinball", "VisualPinball"])
                {
                    string path = _fileSystem.Path.Combine(programFiles, layout);
                    AddIfExists(candidates, path, "A conventional Visual Pinball folder under Program Files.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The known-path discovery layer failed and contributed nothing.");
        }

        return Task.FromResult<IReadOnlyList<InstallationCandidate>>(candidates);
    }

    private IEnumerable<string> GetFixedDriveRoots()
    {
        IDriveInfo[] drives;
        try
        {
            drives = _fileSystem.DriveInfo.GetDrives();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not enumerate drives.");
            yield break;
        }

        foreach (IDriveInfo drive in drives)
        {
            string root;
            try
            {
                // Deliberately a deny-list rather than an allow-list of DriveType.Fixed. Network and
                // removable volumes are skipped because probing them is slow and a disconnected
                // mapped drive can block for a long time. Everything else, including a drive Windows
                // reports as Unknown, is probed: missing a user's installation is a worse outcome
                // than spending a few milliseconds checking a folder that turns out not to exist.
                if (SkippedDriveTypes.Contains(drive.DriveType) || !drive.IsReady)
                {
                    continue;
                }

                root = drive.RootDirectory.FullName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            yield return root;
        }
    }

    private void AddIfExists(List<InstallationCandidate> candidates, string path, string reason)
    {
        try
        {
            if (!_fileSystem.Directory.Exists(path))
            {
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (!candidates.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(new InstallationCandidate(path, InstallationSource.KnownPath, reason));
        }
    }
}
