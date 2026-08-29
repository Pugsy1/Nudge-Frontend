using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Vpx.Platform;

namespace Nudge.Vpx.Roms;

/// <inheritdoc cref="IRomAvailabilityChecker" />
/// <remarks>
/// Reads VPinMAME's <c>rompath</c> the same way <c>Nudge.Vpx.Discovery.RegistryCandidateProvider</c>
/// does for installation discovery (current user's key, falling back to local machine) - see
/// AGENTS.md's "Surrounding ecosystem" notes and docs/RESEARCH-NOTES.md. ROMs are kept zipped and
/// named after the ROM name, so a ROM is "found" when <c>&lt;rompath&gt;\&lt;romname&gt;.zip</c>
/// exists.
/// </remarks>
public sealed class RomAvailabilityChecker : IRomAvailabilityChecker
{
    private const string VPinMameGlobalsKey = @"Software\Freeware\Visual PinMame\globals";
    private const string RomPathValueName = "rompath";

    private readonly IRegistryReader _registry;
    private readonly IFileSystem _fileSystem;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<RomAvailabilityChecker> _logger;

    public RomAvailabilityChecker(
        IRegistryReader registry,
        IFileSystem fileSystem,
        IPathRedactor redactor,
        ILogger<RomAvailabilityChecker> logger)
    {
        _registry = registry;
        _fileSystem = fileSystem;
        _redactor = redactor;
        _logger = logger;
    }

    public Task<RomAvailability> CheckAsync(string romName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(romName);
        cancellationToken.ThrowIfCancellationRequested();

        string? romPath =
            _registry.ReadString(RegistryHiveKind.CurrentUser, VPinMameGlobalsKey, RomPathValueName)
            ?? _registry.ReadString(RegistryHiveKind.LocalMachine, VPinMameGlobalsKey, RomPathValueName);

        if (string.IsNullOrWhiteSpace(romPath))
        {
            _logger.LogDebug("VPinMAME's rompath is not registered on this machine; ROM availability is Unknown.");
            return Task.FromResult(new RomAvailability
            {
                RomName = romName,
                Status = RomAvailabilityStatus.Unknown
            });
        }

        string zipPath = _fileSystem.Path.Combine(romPath, romName + ".zip");
        bool exists = _fileSystem.File.Exists(zipPath);

        _logger.LogDebug(
            "ROM {RomName} checked at {Path}: {Status}",
            romName,
            _redactor.Redact(zipPath),
            exists ? "Found" : "Missing");

        return Task.FromResult(new RomAvailability
        {
            RomName = romName,
            Status = exists ? RomAvailabilityStatus.Found : RomAvailabilityStatus.Missing,
            CheckedPath = zipPath
        });
    }
}
