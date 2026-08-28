using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Works out what a single executable is: flavor, architecture, version and VR capability, with a
/// confidence and the evidence behind it. Returns <see cref="VpxFlavor.Unknown"/> rather than
/// guessing.
/// </summary>
public interface IVpxExecutableIdentifier
{
    /// <summary>
    /// Identifies one executable. Fails only when the file cannot be read at all; an unreadable
    /// classification is a successful result carrying Unknown.
    /// </summary>
    Task<Result<VpxExecutable>> IdentifyAsync(string executablePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Identifies every executable directly inside a folder. Used by installation validation, which
    /// needs the sibling files anyway.
    /// </summary>
    Task<IReadOnlyList<VpxExecutable>> IdentifyFolderAsync(string folderPath, CancellationToken cancellationToken = default);
}
