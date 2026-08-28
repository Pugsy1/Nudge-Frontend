using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Finds Visual Pinball installations. Implementations layer several strategies, each of which
/// produces candidates that are then validated. Nothing is assumed to exist without being probed.
/// </summary>
public interface IVpxInstallationDiscovery
{
    /// <summary>
    /// Runs every automatic discovery layer and returns the installations that validated, best
    /// first. Returns an empty list rather than failing when nothing is found.
    /// </summary>
    Task<IReadOnlyList<VpxInstallation>> DiscoverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a folder the user picked themselves. Fails with a message safe to show in the UI
    /// when the folder is not a Visual Pinball installation.
    /// </summary>
    Task<Result<VpxInstallation>> InspectFolderAsync(string folderPath, CancellationToken cancellationToken = default);
}
