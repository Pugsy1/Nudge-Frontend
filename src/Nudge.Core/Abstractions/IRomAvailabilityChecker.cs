using Nudge.Core.Models;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Checks whether a PinMAME ROM name (see <see cref="IRomNameReader"/>) has a matching file in
/// VPinMAME's configured ROM folder. Implemented by <c>Nudge.Vpx.Roms.RomAvailabilityChecker</c>.
/// Not currently wired into anything - a standalone building block for the health system, Phase 7.
/// </summary>
public interface IRomAvailabilityChecker
{
    /// <summary>
    /// Never fails outright: an undeterminable ROM folder is reported as
    /// <see cref="RomAvailabilityStatus.Unknown"/> rather than an exception, since plenty of real
    /// machines simply don't have VPinMAME's registry key set.
    /// </summary>
    Task<RomAvailability> CheckAsync(string romName, CancellationToken cancellationToken = default);
}
