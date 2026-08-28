using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Launches a table in Visual Pinball and waits for it to exit. Implemented by
/// <c>Nudge.Vpx.Launching.LaunchEngine</c>; this interface knows nothing about
/// <see cref="System.Diagnostics.Process"/> or how an executable is chosen.
/// </summary>
public interface ILaunchEngine
{
    /// <summary>
    /// Launches <paramref name="tablePath"/> using the installation's best desktop-capable
    /// executable (see <see cref="VpxInstallation.BestDesktopExecutable"/>), and returns once
    /// Visual Pinball has exited. VR launching is a separate, later capability (Phase 6).
    /// </summary>
    Task<Result<LaunchOutcome>> LaunchAsync(
        VpxInstallation installation,
        string tablePath,
        CancellationToken cancellationToken = default);
}
