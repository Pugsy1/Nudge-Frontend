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
    /// Visual Pinball has exited.
    /// </summary>
    Task<Result<LaunchOutcome>> LaunchAsync(
        VpxInstallation installation,
        string tablePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches <paramref name="tablePath"/> using a specific executable rather than the
    /// installation's best desktop build - e.g. <see cref="VpxInstallation.BestVrExecutable"/>, so a
    /// headset already on and SteamVR already running let Visual Pinball autodetect VR itself (see
    /// AGENTS.md section 4.3). Nudge does not manage a VR settings/-Ini profile (a separate, later
    /// capability) - this only controls which executable launches, not VR settings themselves.
    /// </summary>
    Task<Result<LaunchOutcome>> LaunchAsync(
        VpxExecutable executable,
        string tablePath,
        CancellationToken cancellationToken = default);
}
