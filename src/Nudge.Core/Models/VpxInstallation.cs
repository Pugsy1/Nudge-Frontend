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
}
