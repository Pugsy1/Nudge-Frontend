namespace Nudge.Core.Models;

/// <summary>
/// One Visual Pinball executable found on disk, and everything Nudge managed to work out about it.
/// </summary>
public sealed record VpxExecutable
{
    /// <summary>Full path to the .exe.</summary>
    public required string Path { get; init; }

    /// <summary>Just the filename, e.g. "VPinballX_GL64.exe".</summary>
    public required string FileName { get; init; }

    public required VpxFlavor Flavor { get; init; }

    /// <summary>Read from the PE header, never inferred from the filename.</summary>
    public required ProcessorArchitecture Architecture { get; init; }

    /// <summary>Win32 version resource FileVersion string, e.g. "10.8.0.2058". Null if absent.</summary>
    public string? FileVersion { get; init; }

    /// <summary>Win32 version resource ProductVersion string. Null if absent.</summary>
    public string? ProductVersion { get; init; }

    /// <summary>Win32 version resource ProductName, e.g. "Visual Pinball". Null if absent.</summary>
    public string? ProductName { get; init; }

    /// <summary>Win32 version resource FileDescription. Null if absent.</summary>
    public string? FileDescription { get; init; }

    /// <summary>The numeric version parsed out of the version resource, when one could be parsed.</summary>
    public Version? ParsedVersion { get; init; }

    public required VrCapability VrCapability { get; init; }

    public required Confidence Confidence { get; init; }

    public required DetectionEvidence Evidence { get; init; }

    /// <summary>
    /// True when the file is recognisably part of Visual Pinball even if the exact build could not
    /// be determined. This is deliberately separate from <see cref="IsRecognised"/>: "this is
    /// Visual Pinball but I cannot tell which flavor" is a different, weaker statement than "this is
    /// the OpenGL build", and Nudge must not collapse the two.
    /// </summary>
    public bool LooksLikeVisualPinball { get; init; }

    /// <summary>True when this executable was classified as a specific Visual Pinball build.</summary>
    public bool IsRecognised => Flavor != VpxFlavor.Unknown;

    /// <summary>Short label for the UI, e.g. "OpenGL (x64)".</summary>
    public string DisplayFlavor => Flavor switch
    {
        VpxFlavor.DirectX9 => "DirectX 9",
        VpxFlavor.OpenGL => "OpenGL",
        VpxFlavor.Bgfx => "BGFX",
        VpxFlavor.VP9Legacy => "Visual Pinball 9 (legacy)",
        _ => "Unknown"
    };

    public string DisplayArchitecture => Architecture switch
    {
        ProcessorArchitecture.X86 => "x86",
        ProcessorArchitecture.X64 => "x64",
        _ => "Unknown"
    };

    public string DisplayVrCapability => VrCapability switch
    {
        VrCapability.None => "No VR",
        VrCapability.OpenVR => "OpenVR (needs SteamVR)",
        VrCapability.OpenXR => "OpenXR",
        _ => "Unknown"
    };

    public string DisplayVersion => FileVersion ?? ProductVersion ?? "Unknown";
}
