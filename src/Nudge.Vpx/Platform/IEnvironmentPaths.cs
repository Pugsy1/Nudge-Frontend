namespace Nudge.Vpx.Platform;

/// <summary>
/// The handful of well-known Windows locations Nudge needs. Behind an interface so tests can point
/// them at an in-memory filesystem instead of the real machine.
/// </summary>
public interface IEnvironmentPaths
{
    /// <summary>%AppData% - where VPinballX.ini lives since Visual Pinball 10.8.</summary>
    string RoamingAppData { get; }

    /// <summary>%LocalAppData% - where Nudge keeps its own settings and logs.</summary>
    string LocalAppData { get; }

    string? ProgramFiles { get; }

    string? ProgramFilesX86 { get; }

    /// <summary>The Windows account name. Used only to redact it back out of logs.</summary>
    string UserName { get; }

    /// <summary>The account's profile folder.</summary>
    string UserProfile { get; }
}

/// <summary>Reads the real environment.</summary>
public sealed class WindowsEnvironmentPaths : IEnvironmentPaths
{
    public string RoamingAppData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public string LocalAppData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public string? ProgramFiles { get; } =
        NullIfBlank(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    public string? ProgramFilesX86 { get; } =
        NullIfBlank(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

    public string UserName { get; } = Environment.UserName;

    public string UserProfile { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
