using Nudge.Core.Models;

namespace Nudge.Core.Abstractions;

/// <summary>
/// Loads and saves the small amount of state Nudge remembers between runs.
/// Implementations must never throw for a missing or corrupt settings file: they fall back to
/// defaults and log, because a broken settings file must not stop the application starting.
/// </summary>
public interface ISettingsService
{
    /// <summary>Reads settings from disk, returning defaults when there is nothing readable there.</summary>
    Task<NudgeSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes settings to disk atomically.</summary>
    Task SaveAsync(NudgeSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads, applies <paramref name="mutate"/>, and saves - as one atomic operation, not a separate
    /// LoadAsync followed by a separate SaveAsync. Any caller that reads settings only to change a
    /// few fields and write the whole object back should use this instead: two callers each doing
    /// their own LoadAsync-then-SaveAsync can interleave (A and B both load the same version, A saves
    /// its change, B then saves its own change on top - silently overwriting A's, a lost update) even
    /// though SaveAsync itself is safe against two writes corrupting the file. MutateAsync holds the
    /// same internal lock across the whole load-mutate-save cycle, so a second caller's MutateAsync
    /// (or SaveAsync) simply waits its turn rather than racing.
    /// </summary>
    Task MutateAsync(Action<NudgeSettings> mutate, CancellationToken cancellationToken = default);

    /// <summary>Full path of the settings file, so the UI and logs can tell the user where it is.</summary>
    string SettingsFilePath { get; }
}
