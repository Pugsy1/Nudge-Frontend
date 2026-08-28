using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Vpx.Platform;
using Nudge.Vpx.Settings;

namespace Nudge.Vpx.Discovery;

/// <summary>
/// Layer 3: directory hints inside the user's own Visual Pinball settings file.
///
/// Since 10.8 the settings live at %AppData%\VPinballX\VPinballX.ini. It records TablesDirectory and
/// friends, which is a statement by Visual Pinball itself about where things are. The tables folder
/// is conventionally directly below the installation root, so its parent is offered as a candidate.
///
/// This file is read only. Nudge never modifies the user's VPinballX.ini - see AGENTS.md section 6.
/// </summary>
public sealed class SettingsFileCandidateProvider : IInstallationCandidateProvider
{
    private const string VpxSettingsFolderName = "VPinballX";
    private const string VpxSettingsFileName = "VPinballX.ini";

    private readonly IFileSystem _fileSystem;
    private readonly IEnvironmentPaths _environment;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<SettingsFileCandidateProvider> _logger;

    public SettingsFileCandidateProvider(
        IFileSystem fileSystem,
        IEnvironmentPaths environment,
        IPathRedactor redactor,
        ILogger<SettingsFileCandidateProvider> logger)
    {
        _fileSystem = fileSystem;
        _environment = environment;
        _redactor = redactor;
        _logger = logger;
    }

    public int Order => 3;

    public string Name => "Visual Pinball settings file";

    /// <summary>Where the settings file is expected to be. Exposed so the UI can say so.</summary>
    public string SettingsFilePath =>
        _fileSystem.Path.Combine(_environment.RoamingAppData, VpxSettingsFolderName, VpxSettingsFileName);

    public async Task<IReadOnlyList<InstallationCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<InstallationCandidate>();

        try
        {
            string iniPath = SettingsFilePath;
            if (!_fileSystem.File.Exists(iniPath))
            {
                _logger.LogDebug("No Visual Pinball settings file at {IniPath}", _redactor.Redact(iniPath));
                return candidates;
            }

            VpxIniFile ini = await VpxIniFile.ReadAsync(_fileSystem, iniPath, cancellationToken).ConfigureAwait(false);

            foreach ((string key, string value) in ini.GetDirectoryHints())
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? directory = NormaliseHint(value);
                if (directory is null)
                {
                    continue;
                }

                // The hinted folder itself, in case it is the installation root.
                AddIfExists(
                    candidates,
                    directory,
                    $"VPinballX.ini records {key} as this folder.");

                // More usefully, its parent: Tables, Scripts and Music all sit directly below the root.
                string? parent = TryGetParent(directory);
                if (parent is not null)
                {
                    AddIfExists(
                        candidates,
                        parent,
                        $"VPinballX.ini records {key} as a folder directly below this one.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The settings-file discovery layer failed and contributed nothing.");
        }

        return candidates;
    }

    /// <summary>
    /// Turns an ini value into a usable absolute directory path, or null when it is not one.
    /// Relative paths are rejected rather than resolved: they are relative to whatever working
    /// directory Visual Pinball had, which Nudge cannot know.
    /// </summary>
    private string? NormaliseHint(string value)
    {
        string trimmed = value.Trim().Trim('"').TrimEnd('\\', '/');

        if (trimmed.Length == 0)
        {
            return null;
        }

        try
        {
            return _fileSystem.Path.IsPathFullyQualified(trimmed) ? trimmed : null;
        }
        catch (ArgumentException)
        {
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
            candidates.Add(new InstallationCandidate(path, InstallationSource.SettingsFile, reason));
        }
    }
}
