namespace Nudge.Core.Models;

/// <summary>
/// Which rendering build of Visual Pinball X an executable is. See docs/RESEARCH-NOTES.md.
/// </summary>
public enum VpxFlavor
{
    /// <summary>Could not be classified. Never guessed.</summary>
    Unknown = 0,

    /// <summary>The DirectX 9 reference build, typically VPinballX.exe. No VR.</summary>
    DirectX9,

    /// <summary>The OpenGL build, typically VPinballX_GL.exe / VPinballX_GL64.exe. VR via OpenVR.</summary>
    OpenGL,

    /// <summary>The BGFX build, typically VPinballX_BGFX.exe. VR via OpenXR from 10.8.1.</summary>
    Bgfx,

    /// <summary>Visual Pinball 9, typically VPinball995.exe. Plays .vpt tables. No VR.</summary>
    VP9Legacy
}

/// <summary>Machine architecture, read from the PE header rather than the filename.</summary>
public enum ProcessorArchitecture
{
    Unknown = 0,
    X86,
    X64
}

/// <summary>What VR runtime, if any, a build can drive.</summary>
public enum VrCapability
{
    /// <summary>Not determined. Reported honestly rather than assumed.</summary>
    Unknown = 0,

    /// <summary>This build cannot do VR at all.</summary>
    None,

    /// <summary>OpenVR, which in practice means SteamVR must be installed.</summary>
    OpenVR,

    /// <summary>OpenXR, the current path used by the BGFX build.</summary>
    OpenXR
}

/// <summary>
/// How much Nudge trusts a detection result. Always shown to the user alongside the result.
/// </summary>
public enum Confidence
{
    Unknown = 0,
    Low,
    Medium,
    High
}

/// <summary>Which discovery layer produced an installation candidate.</summary>
public enum InstallationSource
{
    Unknown = 0,

    /// <summary>Windows registry: COM registration or VPinMAME's rompath.</summary>
    Registry,

    /// <summary>A conventional install location that was probed and found to exist.</summary>
    KnownPath,

    /// <summary>A directory hint read out of VPinballX.ini.</summary>
    SettingsFile,

    /// <summary>The user pointed at the folder themselves. Always the final authority.</summary>
    Manual
}
