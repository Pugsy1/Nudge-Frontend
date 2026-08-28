namespace Nudge.Core.Models;

/// <summary>
/// Everything Nudge remembers between runs in Phase 1. Persisted as JSON.
/// </summary>
public sealed class NudgeSettings
{
    /// <summary>Bumped when the shape of this file changes, so old files can be migrated.</summary>
    public int SettingsVersion { get; set; } = 1;

    /// <summary>Id of the installation the user confirmed. Null until they confirm one.</summary>
    public string? SelectedInstallationId { get; set; }

    /// <summary>
    /// Root path of the confirmed installation. Stored alongside the id so a settings file remains
    /// readable by a human and recoverable if ids ever change.
    /// </summary>
    public string? SelectedInstallationPath { get; set; }

    /// <summary>"Dark" or "Light".</summary>
    public string ThemeName { get; set; } = "Dark";

    /// <summary>Installations the user has confirmed or added manually, newest last.</summary>
    public List<KnownInstallation> KnownInstallations { get; set; } = [];
}

/// <summary>A remembered installation. Deliberately minimal: paths get re-validated on every start.</summary>
public sealed class KnownInstallation
{
    public string Id { get; set; } = string.Empty;

    public string RootPath { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset DateAdded { get; set; }

    public bool IsDefault { get; set; }
}
