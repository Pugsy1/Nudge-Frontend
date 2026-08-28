using System.IO.Abstractions;

namespace Nudge.Vpx.Identification;

/// <summary>
/// What else is sitting in the folder next to an executable. Flavor-specific support libraries
/// corroborate a filename hint, which is how a Medium confidence becomes a High one.
/// </summary>
/// <remarks>
/// These are folder-level facts, not file-level ones. The presence of openvr_api64.dll says the
/// folder contains an OpenVR-capable build; it does not prove that any <em>particular</em> exe in
/// the folder is that build. They are therefore only ever used as supporting evidence.
/// </remarks>
internal sealed record SiblingLibraries
{
    public required IReadOnlySet<string> FileNames { get; init; }

    /// <summary>openvr_api.dll / openvr_api64.dll, shipped with the OpenVR-era OpenGL build.</summary>
    public bool HasOpenVrRuntime { get; init; }

    /// <summary>Any library with "bgfx" in its name.</summary>
    public bool HasBgfxLibraries { get; init; }

    /// <summary>VPinMAME.dll, the emulator used for solid-state tables.</summary>
    public bool HasVPinMame { get; init; }

    /// <summary>A B2S backglass server component.</summary>
    public bool HasB2SServer { get; init; }

    public static SiblingLibraries Empty { get; } = new()
    {
        FileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    };

    /// <summary>
    /// Scans a folder's immediate contents. Failures are swallowed into an empty result: a folder we
    /// cannot list is a missing corroboration, not an error worth surfacing.
    /// </summary>
    public static SiblingLibraries Scan(IFileSystem fileSystem, string folderPath)
    {
        try
        {
            if (!fileSystem.Directory.Exists(folderPath))
            {
                return Empty;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string fullPath in fileSystem.Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly))
            {
                string? name = fileSystem.Path.GetFileName(fullPath);
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            return new SiblingLibraries
            {
                FileNames = names,
                HasOpenVrRuntime = names.Any(n =>
                    n.StartsWith("openvr_api", StringComparison.OrdinalIgnoreCase)
                    && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)),
                HasBgfxLibraries = names.Any(n =>
                    n.Contains("bgfx", StringComparison.OrdinalIgnoreCase)
                    && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)),
                HasVPinMame = names.Contains("VPinMAME.dll"),
                HasB2SServer = names.Any(n => n.StartsWith("B2S", StringComparison.OrdinalIgnoreCase))
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }
}
