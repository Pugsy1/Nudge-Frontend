using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;
using Nudge.Vpx.Platform;
using Nudge.Vpx.Settings;

namespace Nudge.Vpx.Discovery;

/// <summary>
/// Decides whether a candidate folder really is a Visual Pinball installation.
///
/// The test is deliberately concrete: the folder must contain at least one executable that Nudge
/// recognises as part of Visual Pinball. A plausible tables folder raises confidence but is not
/// required, because a fresh install has no tables in it yet.
/// </summary>
public sealed class InstallationValidator
{
    private const string PortableSettingsFileName = "VPinballX.ini";
    private const string ConventionalTablesFolderName = "Tables";

    private readonly IFileSystem _fileSystem;
    private readonly IVpxExecutableIdentifier _identifier;
    private readonly IEnvironmentPaths _environment;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<InstallationValidator> _logger;

    public InstallationValidator(
        IFileSystem fileSystem,
        IVpxExecutableIdentifier identifier,
        IEnvironmentPaths environment,
        IPathRedactor redactor,
        ILogger<InstallationValidator> logger)
    {
        _fileSystem = fileSystem;
        _identifier = identifier;
        _environment = environment;
        _redactor = redactor;
        _logger = logger;
    }

    public async Task<Result<VpxInstallation>> ValidateAsync(
        InstallationCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        return await ValidateAsync(candidate, [candidate.Reason], cancellationToken).ConfigureAwait(false);
    }

    /// <param name="reasons">
    /// Every reason this folder came up, which can be more than one when several discovery layers
    /// independently pointed at it. Agreement between layers is itself evidence.
    /// </param>
    public async Task<Result<VpxInstallation>> ValidateAsync(
        InstallationCandidate candidate,
        IReadOnlyList<string> reasons,
        CancellationToken cancellationToken = default)
    {
        string path = candidate.Path;

        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<VpxInstallation>.Failure("No folder was given.");
        }

        if (!DirectoryExists(path))
        {
            return Result<VpxInstallation>.Failure($"The folder '{path}' does not exist, or cannot be read.");
        }

        var evidence = DetectionEvidence.Empty();
        foreach (string reason in reasons)
        {
            evidence.Add(SourceLabel(candidate.Source), reason);
        }

        IReadOnlyList<VpxExecutable> executables =
            await _identifier.IdentifyFolderAsync(path, cancellationToken).ConfigureAwait(false);

        var recognised = executables.Where(e => e.IsRecognised).ToList();
        var looksLikeVpx = executables.Where(e => !e.IsRecognised && e.LooksLikeVisualPinball).ToList();

        if (recognised.Count == 0 && looksLikeVpx.Count == 0)
        {
            _logger.LogDebug(
                "Rejected {Path}: {ExecutableCount} executables, none recognisable as Visual Pinball",
                _redactor.Redact(path),
                executables.Count);

            return Result<VpxInstallation>.Failure(
                executables.Count == 0
                    ? $"'{path}' contains no programs, so it is not a Visual Pinball installation."
                    : $"'{path}' contains {executables.Count} program(s), but none of them is Visual Pinball.");
        }

        evidence.Add(
            "Executables",
            recognised.Count > 0
                ? $"Found {recognised.Count} recognised Visual Pinball executable(s): "
                  + string.Join(", ", recognised.Select(e => $"{e.FileName} ({e.DisplayFlavor}, {e.DisplayArchitecture})"))
                  + "."
                : $"Found {looksLikeVpx.Count} executable(s) that look like Visual Pinball but whose build "
                  + "could not be identified.",
            recognised.Count > 0 ? EvidenceWeight.Decisive : EvidenceWeight.Supporting);

        string? tablesPath = await ResolveTablesPathAsync(path, evidence, cancellationToken).ConfigureAwait(false);

        Confidence confidence = ScoreInstallation(recognised, looksLikeVpx, tablesPath is not null);

        var installation = new VpxInstallation
        {
            Id = BuildStableId(path),
            DisplayName = BuildDisplayName(path),
            RootPath = path,
            TablesPath = tablesPath,
            Executables = executables,
            DiscoverySource = candidate.Source,
            Confidence = confidence,
            Evidence = evidence,
            DateAdded = DateTimeOffset.Now,
            IsDefault = false
        };

        return Result<VpxInstallation>.Success(installation);
    }

    /// <summary>
    /// Works out where this installation's tables live.
    ///
    /// Order matters. A VPinballX.ini sitting beside the executables is portable mode and describes
    /// <em>this</em> installation, so it is trusted first. The conventional Tables subfolder comes
    /// next. The user-wide settings file is consulted last, because a machine has only one of those
    /// but may have several Visual Pinball installations.
    /// </summary>
    private async Task<string?> ResolveTablesPathAsync(
        string root,
        DetectionEvidence evidence,
        CancellationToken cancellationToken)
    {
        string portableIni = _fileSystem.Path.Combine(root, PortableSettingsFileName);
        if (FileExists(portableIni))
        {
            VpxIniFile ini = await VpxIniFile.ReadAsync(_fileSystem, portableIni, cancellationToken).ConfigureAwait(false);
            string? hinted = ini.FindValue("TablesDirectory");

            if (!string.IsNullOrWhiteSpace(hinted) && DirectoryExists(hinted))
            {
                evidence.Add(
                    "Tables folder",
                    $"A VPinballX.ini beside the executables (portable mode) points at '{hinted}'.",
                    EvidenceWeight.Decisive);
                return hinted;
            }
        }

        string conventional = _fileSystem.Path.Combine(root, ConventionalTablesFolderName);
        if (DirectoryExists(conventional))
        {
            evidence.Add("Tables folder", $"Found the conventional Tables folder at '{conventional}'.");
            return conventional;
        }

        string userIni = _fileSystem.Path.Combine(_environment.RoamingAppData, "VPinballX", PortableSettingsFileName);
        if (FileExists(userIni))
        {
            VpxIniFile ini = await VpxIniFile.ReadAsync(_fileSystem, userIni, cancellationToken).ConfigureAwait(false);
            string? hinted = ini.FindValue("TablesDirectory");

            if (!string.IsNullOrWhiteSpace(hinted) && DirectoryExists(hinted))
            {
                evidence.Add(
                    "Tables folder",
                    $"This installation has no Tables subfolder, but the user-wide VPinballX.ini points at "
                    + $"'{hinted}'. That setting is shared by every Visual Pinball installation on this "
                    + "machine, so it may belong to a different one.",
                    EvidenceWeight.Supporting);
                return hinted;
            }
        }

        evidence.Add(
            "Tables folder",
            "No tables folder was found. This is normal for a fresh installation.",
            EvidenceWeight.Informational);

        return null;
    }

    private static Confidence ScoreInstallation(
        IReadOnlyList<VpxExecutable> recognised,
        IReadOnlyList<VpxExecutable> looksLikeVpx,
        bool hasTablesFolder)
    {
        if (recognised.Count == 0)
        {
            // Something Visual-Pinball-shaped, but nothing Nudge can name. Honest answer: Low.
            return looksLikeVpx.Count > 0 ? Confidence.Low : Confidence.Unknown;
        }

        Confidence best = recognised.Max(e => e.Confidence);

        return best switch
        {
            Confidence.High when hasTablesFolder => Confidence.High,
            Confidence.High => Confidence.Medium,
            Confidence.Medium when hasTablesFolder => Confidence.Medium,
            Confidence.Medium => Confidence.Low,
            _ => Confidence.Low
        };
    }

    /// <summary>
    /// A stable identifier for an installation, derived from its path so it is the same on every
    /// run without needing to be stored anywhere.
    /// </summary>
    internal string BuildStableId(string rootPath)
    {
        string normalised = NormalisePath(rootPath);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
        return Convert.ToHexStringLower(hash)[..12];
    }

    internal string NormalisePath(string path)
    {
        try
        {
            string full = _fileSystem.Path.GetFullPath(path);
            string trimmed = full.TrimEnd('\\', '/');

            // A drive root trims down to "C:", which is a different thing from "C:\". Put it back.
            return (trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + '\\' : trimmed)
                .ToLowerInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return path.TrimEnd('\\', '/').ToLowerInvariant();
        }
    }

    private string BuildDisplayName(string path)
    {
        string trimmed = path.TrimEnd('\\', '/');
        string? name = _fileSystem.Path.GetFileName(trimmed);

        // A drive root has no folder name of its own.
        return string.IsNullOrWhiteSpace(name) ? trimmed + '\\' : name;
    }

    private bool DirectoryExists(string path)
    {
        try
        {
            return _fileSystem.Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private bool FileExists(string path)
    {
        try
        {
            return _fileSystem.File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string SourceLabel(InstallationSource source) => source switch
    {
        InstallationSource.Registry => "Registry",
        InstallationSource.KnownPath => "Known location",
        InstallationSource.SettingsFile => "Visual Pinball settings",
        InstallationSource.Manual => "Chosen by you",
        _ => "Discovery"
    };
}
