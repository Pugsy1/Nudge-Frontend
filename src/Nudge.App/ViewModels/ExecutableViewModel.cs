using Nudge.Core.Models;

namespace Nudge.App.ViewModels;

/// <summary>
/// One row in the "Show details" executables table.
/// </summary>
/// <remarks>
/// A plain projection of <see cref="VpxExecutable"/>. It holds no logic of its own: everything shown
/// here was decided by the identification service, which is where it can be tested.
/// </remarks>
public sealed class ExecutableViewModel
{
    private readonly VpxExecutable _executable;

    public ExecutableViewModel(VpxExecutable executable) => _executable = executable;

    public string FileName => _executable.FileName;

    public string Flavor => _executable.DisplayFlavor;

    public string Architecture => _executable.DisplayArchitecture;

    public string Version => _executable.DisplayVersion;

    public Confidence Confidence => _executable.Confidence;

    public string ConfidenceLabel => _executable.Confidence switch
    {
        Core.Models.Confidence.High => "High",
        Core.Models.Confidence.Medium => "Medium",
        Core.Models.Confidence.Low => "Low",
        _ => "Unknown"
    };

    public string FullPath => _executable.Path;
}
