using Nudge.Core.Models;

namespace Nudge.Vpx.Discovery;

/// <summary>
/// A folder that <em>might</em> be a Visual Pinball installation, and why we think so.
///
/// Candidates are deliberately cheap and deliberately wrong sometimes. Every discovery layer
/// produces candidates; a separate validation step decides which of them are real. Nothing is
/// reported to the user until it has been probed on disk.
/// </summary>
/// <param name="Path">The folder to probe.</param>
/// <param name="Source">Which discovery layer suggested it.</param>
/// <param name="Reason">Human-readable explanation, shown as evidence.</param>
public sealed record InstallationCandidate(string Path, InstallationSource Source, string Reason);

/// <summary>One layer of the discovery strategy.</summary>
public interface IInstallationCandidateProvider
{
    /// <summary>
    /// Lower numbers run first and win when two layers suggest the same folder. Registry evidence is
    /// more reliable than probing conventional paths, which is more reliable than an ini hint.
    /// </summary>
    int Order { get; }

    string Name { get; }

    /// <summary>
    /// Produces candidates. Must never throw: a discovery layer that fails is one that contributes
    /// nothing, not one that breaks startup.
    /// </summary>
    Task<IReadOnlyList<InstallationCandidate>> GetCandidatesAsync(CancellationToken cancellationToken = default);
}
