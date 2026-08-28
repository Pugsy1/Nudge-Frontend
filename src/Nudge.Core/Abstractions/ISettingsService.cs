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

    /// <summary>Full path of the settings file, so the UI and logs can tell the user where it is.</summary>
    string SettingsFilePath { get; }
}
