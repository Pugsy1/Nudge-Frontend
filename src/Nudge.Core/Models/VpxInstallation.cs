namespace Nudge.Core.Models;

/// <summary>
/// A folder on disk that Nudge believes is a Visual Pinball X installation, plus the executables
/// inside it and the reasoning that led here.
/// </summary>
public sealed record VpxInstallation
{
    /// <summary>Stable identifier derived from the normalised root path, so it survives restarts.</summary>
    public required string Id { get; init; }

    /// <summary>Name shown in the UI. Defaults to the folder name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Full path to the installation root, the folder holding the executables.</summary>
    public required string RootPath { get; init; }

    /// <summary>
    /// Where tables live. Taken from VPinballX.ini when it says, otherwise the conventional
    /// &lt;root&gt;\Tables. Null when no plausible tables folder was found.
    /// </summary>
    public string? TablesPath { get; init; }

    public required IReadOnlyList<VpxExecutable> Executables { get; init; }

    /// <summary>True for the installation Nudge will use unless the user says otherwise.</summary>
    public bool IsDefault { get; init; }

    public DateTimeOffset DateAdded { get; init; }

    public required InstallationSource DiscoverySource { get; init; }

    public required Confidence Confidence { get; init; }

    public required DetectionEvidence Evidence { get; init; }

    public IEnumerable<VpxExecutable> RecognisedExecutables => Executables.Where(e => e.IsRecognised);

    public bool HasTablesFolder => !string.IsNullOrWhiteSpace(TablesPath);

    /// <summary>The best VR capability offered by any executable here. Unknown when nothing is sure.</summary>
    public VrCapability BestVrCapability
    {
        get
        {
            var capabilities = RecognisedExecutables.Select(e => e.VrCapability).ToList();
            if (capabilities.Contains(VrCapability.OpenXR))
            {
                return VrCapability.OpenXR;
            }

            if (capabilities.Contains(VrCapability.OpenVR))
            {
                return VrCapability.OpenVR;
            }

            if (capabilities.Count > 0 && capabilities.All(c => c == VrCapability.None))
            {
                return VrCapability.None;
            }

            return VrCapability.Unknown;
        }
    }

    /// <summary>
    /// The executable Nudge will launch for a normal desktop session: the most modern recognised
    /// build available (currently BGFX, then DirectX 9), preferring x64 over x86 when a flavor is
    /// available in both. Null when nothing here can be trusted to launch to the desktop.
    /// </summary>
    /// <remarks>
    /// Two flavors are deliberately excluded, even as a last resort:
    /// <list type="bullet">
    /// <item><see cref="VpxFlavor.VP9Legacy"/> plays the older <c>.vpt</c> format, not <c>.vpx</c> -
    /// per docs/RESEARCH-NOTES.md - so launching it against a scanned <c>.vpx</c> table would not
    /// fail loudly, it would just be wrong.</item>
    /// <item><see cref="VpxFlavor.OpenGL"/> is excluded from Desktop selection specifically because
    /// of a real, observed behaviour on a Baller-installed machine: per AGENTS.md section 4.3, "the
    /// OpenGL build autodetects a SteamVR driver install" and launches straight into VR with no
    /// command-line flag involved - there is no way to tell a plain "-Play" launch will stay on the
    /// desktop until Phase 6 builds proper VR profile / "-Ini" control. Until then, an OpenGL-only
    /// installation reports no Desktop build available rather than risk silently launching VR.</item>
    /// </list>
    /// </remarks>
    public VpxExecutable? BestDesktopExecutable => RecognisedExecutables
        .Where(e => DesktopFlavorRank(e.Flavor) >= 0)
        .OrderByDescending(e => DesktopFlavorRank(e.Flavor))
        .ThenByDescending(e => e.Architecture == ProcessorArchitecture.X64)
        .FirstOrDefault();

    private static int DesktopFlavorRank(VpxFlavor flavor) => flavor switch
    {
        VpxFlavor.Bgfx => 1,
        VpxFlavor.DirectX9 => 0,
        _ => -1
    };
}
