using Nudge.Vpx.Identification;
using Nudge.Vpx.Platform;

namespace Nudge.TestSupport;

/// <summary>
/// Version resources scripted per path.
///
/// Win32 version resources can only be read through the operating system, so they cannot be faked
/// inside an in-memory filesystem. Tests script them here instead.
/// </summary>
public sealed class FakeFileVersionInfoReader : IFileVersionInfoReader
{
    private readonly Dictionary<string, FileVersionDetails> _byPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records what the version resource of a given file should say.</summary>
    public FakeFileVersionInfoReader Set(string path, FileVersionDetails details)
    {
        _byPath[path] = details;
        return this;
    }

    /// <summary>Convenience for the common "this is Visual Pinball 10.8.0" case.</summary>
    public FakeFileVersionInfoReader SetVisualPinball(
        string path,
        string version = "10.8.0.2058",
        string productName = "Visual Pinball",
        string? fileDescription = null)
    {
        return Set(path, new FileVersionDetails
        {
            ProductName = productName,
            FileDescription = fileDescription ?? productName,
            FileVersion = version,
            ProductVersion = version,
            CompanyName = "Visual Pinball",
            NumericFileVersion = Version.Parse(version)
        });
    }

    public Task<FileVersionDetails> ReadAsync(string executablePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byPath.TryGetValue(executablePath, out FileVersionDetails? details)
            ? details
            : FileVersionDetails.Empty);
}

/// <summary>Environment locations pointed at wherever a test wants them.</summary>
public sealed class FakeEnvironmentPaths : IEnvironmentPaths
{
    public string RoamingAppData { get; set; } = @"C:\Users\TestUser\AppData\Roaming";

    public string LocalAppData { get; set; } = @"C:\Users\TestUser\AppData\Local";

    public string? ProgramFiles { get; set; } = @"C:\Program Files";

    public string? ProgramFilesX86 { get; set; } = @"C:\Program Files (x86)";

    public string UserName { get; set; } = "TestUser";

    public string UserProfile { get; set; } = @"C:\Users\TestUser";
}

/// <summary>
/// A registry that contains only what a test puts in it. Empty by default, so tests that do not
/// care about the registry get a machine with no Visual Pinball registrations at all.
/// </summary>
public sealed class FakeRegistryReader : IRegistryReader
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _subKeys = new(StringComparer.OrdinalIgnoreCase);

    public FakeRegistryReader SetValue(RegistryHiveKind hive, string keyPath, string? valueName, string value)
    {
        _values[BuildKey(hive, keyPath, valueName)] = value;
        return this;
    }

    public FakeRegistryReader SetSubKeys(RegistryHiveKind hive, string keyPath, params string[] names)
    {
        _subKeys[BuildKey(hive, keyPath, null)] = [.. names];
        return this;
    }

    /// <summary>Sets up a COM registration the way a real installer would: ProgID, CLSID, server path.</summary>
    public FakeRegistryReader SetComServer(string progId, string clsid, string serverPath, bool inProcess = false)
    {
        SetValue(RegistryHiveKind.ClassesRoot, $@"{progId}\CLSID", null, clsid);
        SetValue(
            RegistryHiveKind.ClassesRoot,
            $@"CLSID\{clsid}\{(inProcess ? "InprocServer32" : "LocalServer32")}",
            null,
            serverPath);
        return this;
    }

    public string? ReadString(RegistryHiveKind hive, string keyPath, string? valueName) =>
        _values.TryGetValue(BuildKey(hive, keyPath, valueName), out string? value) ? value : null;

    public IReadOnlyList<string> ReadSubKeyNames(RegistryHiveKind hive, string keyPath) =>
        _subKeys.TryGetValue(BuildKey(hive, keyPath, null), out List<string>? names) ? names : [];

    private static string BuildKey(RegistryHiveKind hive, string keyPath, string? valueName) =>
        $"{hive}|{keyPath}|{valueName ?? "(default)"}";
}
