using System.Diagnostics;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Vpx.Discovery;

/// <summary>
/// Runs every discovery layer, merges what they suggest, and validates the result.
///
/// The layers only ever produce candidates. This class is the only thing that decides an
/// installation is real, and it decides by looking at the disk.
/// </summary>
public sealed class VpxInstallationDiscovery : IVpxInstallationDiscovery
{
    /// <summary>
    /// Subfolder names worth checking when the user picks a folder that turns out to be one level
    /// above the real installation. Users routinely pick "C:\vPinball" when the executables are in
    /// "C:\vPinball\VisualPinball".
    /// </summary>
    private static readonly string[] ManualPickChildFolders =
    [
        "VisualPinball",
        "Visual Pinball",
        "VPinballX",
        "VPX"
    ];

    private readonly IReadOnlyList<IInstallationCandidateProvider> _providers;
    private readonly InstallationValidator _validator;
    private readonly IFileSystem _fileSystem;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<VpxInstallationDiscovery> _logger;

    public VpxInstallationDiscovery(
        IEnumerable<IInstallationCandidateProvider> providers,
        InstallationValidator validator,
        IFileSystem fileSystem,
        IPathRedactor redactor,
        ILogger<VpxInstallationDiscovery> logger)
    {
        _providers = providers.OrderBy(p => p.Order).ToList();
        _validator = validator;
        _fileSystem = fileSystem;
        _redactor = redactor;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VpxInstallation>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting Visual Pinball discovery across {LayerCount} layers.", _providers.Count);

        // Path (normalised) -> the candidate that got there first, plus every reason offered.
        var merged = new Dictionary<string, MergedCandidate>(StringComparer.Ordinal);

        foreach (IInstallationCandidateProvider provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<InstallationCandidate> candidates;
            try
            {
                candidates = await provider.GetCandidatesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discovery layer '{Layer}' failed and was skipped.", provider.Name);
                continue;
            }

            _logger.LogDebug("Layer '{Layer}' suggested {Count} candidate(s).", provider.Name, candidates.Count);

            foreach (InstallationCandidate candidate in candidates)
            {
                string key = _validator.NormalisePath(candidate.Path);

                if (merged.TryGetValue(key, out MergedCandidate? existing))
                {
                    // A second layer agreeing is worth recording as evidence.
                    existing.Reasons.Add(candidate.Reason);
                }
                else
                {
                    merged[key] = new MergedCandidate(candidate, [candidate.Reason]);
                }
            }
        }

        _logger.LogDebug("Validating {Count} distinct candidate folder(s).", merged.Count);

        var installations = new List<VpxInstallation>();

        foreach (MergedCandidate entry in merged.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Result<VpxInstallation> result = await _validator
                .ValidateAsync(entry.Candidate, entry.Reasons, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                installations.Add(result.Value);
            }
        }

        List<VpxInstallation> ordered = installations
            .OrderByDescending(i => i.Confidence)
            .ThenBy(i => i.DiscoverySource)
            .ThenBy(i => i.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The best one is the default unless the user says otherwise.
        if (ordered.Count > 0)
        {
            ordered[0] = ordered[0] with { IsDefault = true };
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Discovery finished in {ElapsedMs} ms: {Found} installation(s) from {Probed} candidate folder(s).",
            stopwatch.ElapsedMilliseconds,
            ordered.Count,
            merged.Count);

        foreach (VpxInstallation installation in ordered)
        {
            _logger.LogInformation(
                "  {DisplayName} at {RootPath} - {ExecutableCount} executable(s), confidence {Confidence}",
                installation.DisplayName,
                _redactor.Redact(installation.RootPath),
                installation.Executables.Count,
                installation.Confidence);
        }

        return ordered;
    }

    public async Task<Result<VpxInstallation>> InspectFolderAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return Result<VpxInstallation>.Failure("No folder was chosen.");
        }

        _logger.LogInformation("Inspecting folder chosen by the user: {FolderPath}", _redactor.Redact(folderPath));

        var candidate = new InstallationCandidate(
            folderPath,
            InstallationSource.Manual,
            "You chose this folder.");

        Result<VpxInstallation> result = await _validator.ValidateAsync(candidate, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return Result<VpxInstallation>.Success(result.Value with { IsDefault = true });
        }

        // The folder itself is not an installation. Before giving up, check the subfolders users
        // commonly pick one level too high.
        Result<VpxInstallation> childResult =
            await TryChildFoldersAsync(folderPath, cancellationToken).ConfigureAwait(false);

        if (childResult.IsSuccess)
        {
            return childResult;
        }

        _logger.LogInformation(
            "Rejected the chosen folder {FolderPath}: {Reason}",
            _redactor.Redact(folderPath),
            result.Error);

        return result;
    }

    private async Task<Result<VpxInstallation>> TryChildFoldersAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        foreach (string childName in ManualPickChildFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string childPath;
            try
            {
                childPath = _fileSystem.Path.Combine(folderPath, childName);
                if (!_fileSystem.Directory.Exists(childPath))
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            var childCandidate = new InstallationCandidate(
                childPath,
                InstallationSource.Manual,
                $"You chose the folder above this one, and '{childName}' inside it is a Visual Pinball "
                + "installation.");

            Result<VpxInstallation> childResult = await _validator
                .ValidateAsync(childCandidate, cancellationToken)
                .ConfigureAwait(false);

            if (childResult.IsSuccess)
            {
                _logger.LogInformation(
                    "The chosen folder was one level too high; using {ChildPath} instead.",
                    _redactor.Redact(childPath));

                return Result<VpxInstallation>.Success(childResult.Value with { IsDefault = true });
            }
        }

        return Result<VpxInstallation>.Failure("No Visual Pinball installation was found in that folder.");
    }

    private sealed record MergedCandidate(InstallationCandidate Candidate, List<string> Reasons);
}
