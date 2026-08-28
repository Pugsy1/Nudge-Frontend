using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Nudge.Vpx.Platform;

public enum RegistryHiveKind
{
    ClassesRoot,
    CurrentUser,
    LocalMachine
}

/// <summary>
/// Read-only access to the Windows registry.
///
/// Nudge only ever reads. It does not register COM objects, does not repair other applications'
/// registrations, and does not require administrator rights.
/// </summary>
public interface IRegistryReader
{
    /// <summary>
    /// Reads a string value, or null when the key or value is absent. Pass null for
    /// <paramref name="valueName"/> to read the key's default value.
    /// </summary>
    string? ReadString(RegistryHiveKind hive, string keyPath, string? valueName);

    /// <summary>Names of a key's immediate subkeys, empty when the key is absent.</summary>
    IReadOnlyList<string> ReadSubKeyNames(RegistryHiveKind hive, string keyPath);
}

/// <summary>
/// Registry reader over the real registry.
/// </summary>
/// <remarks>
/// Every lookup is tried in the 64-bit view and then the 32-bit view. Visual Pinball ships both
/// 32- and 64-bit builds, and VPinMAME is a 32-bit COM server, so its registration lands under
/// Wow6432Node on a 64-bit machine. Probing both views means callers never have to think about it.
/// </remarks>
public sealed class WindowsRegistryReader : IRegistryReader
{
    private static readonly RegistryView[] Views = [RegistryView.Registry64, RegistryView.Registry32];

    private readonly ILogger<WindowsRegistryReader> _logger;

    public WindowsRegistryReader(ILogger<WindowsRegistryReader> logger) => _logger = logger;

    public string? ReadString(RegistryHiveKind hive, string keyPath, string? valueName)
    {
        foreach (RegistryView view in Views)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(ToHive(hive), view);
                using RegistryKey? key = baseKey.OpenSubKey(keyPath, writable: false);

                if (key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // A key we are not allowed to read is a dead end, not a failure worth surfacing.
                _logger.LogDebug(ex, "Could not read {Hive}\\{KeyPath} in the {View} view", hive, keyPath, view);
            }
        }

        return null;
    }

    public IReadOnlyList<string> ReadSubKeyNames(RegistryHiveKind hive, string keyPath)
    {
        foreach (RegistryView view in Views)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(ToHive(hive), view);
                using RegistryKey? key = baseKey.OpenSubKey(keyPath, writable: false);

                if (key is not null)
                {
                    string[] names = key.GetSubKeyNames();
                    if (names.Length > 0)
                    {
                        return names;
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                _logger.LogDebug(ex, "Could not enumerate {Hive}\\{KeyPath} in the {View} view", hive, keyPath, view);
            }
        }

        return [];
    }

    private static RegistryHive ToHive(RegistryHiveKind kind) => kind switch
    {
        RegistryHiveKind.ClassesRoot => RegistryHive.ClassesRoot,
        RegistryHiveKind.CurrentUser => RegistryHive.CurrentUser,
        RegistryHiveKind.LocalMachine => RegistryHive.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled registry hive.")
    };
}
