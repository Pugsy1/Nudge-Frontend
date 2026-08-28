using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nudge.Core.Models;

namespace Nudge.App.ViewModels;

/// <summary>One detected Visual Pinball installation, as shown on the setup screen.</summary>
public sealed partial class InstallationViewModel : ObservableObject
{
    public InstallationViewModel(VpxInstallation installation)
    {
        Installation = installation;
        Executables = new ObservableCollection<ExecutableViewModel>(
            installation.Executables.Select(e => new ExecutableViewModel(e)));
    }

    public VpxInstallation Installation { get; }

    public ObservableCollection<ExecutableViewModel> Executables { get; }

    public string Id => Installation.Id;

    public string DisplayName => Installation.DisplayName;

    public string RootPath => Installation.RootPath;

    public string TablesPathDisplay => Installation.TablesPath ?? "No tables folder found";

    public bool HasTablesFolder => Installation.HasTablesFolder;

    public Confidence Confidence => Installation.Confidence;

    public string ConfidenceLabel => Installation.Confidence switch
    {
        Core.Models.Confidence.High => "High confidence",
        Core.Models.Confidence.Medium => "Medium confidence",
        Core.Models.Confidence.Low => "Low confidence",
        _ => "Unknown confidence"
    };

    public string DiscoverySourceLabel => Installation.DiscoverySource switch
    {
        InstallationSource.Registry => "Found in the Windows registry",
        InstallationSource.KnownPath => "Found in a conventional location",
        InstallationSource.SettingsFile => "Found via VPinballX.ini",
        InstallationSource.Manual => "Chosen by you",
        _ => "Found by Nudge"
    };

    public string ExecutableSummary
    {
        get
        {
            int recognised = Installation.RecognisedExecutables.Count();
            int total = Installation.Executables.Count;

            return total == recognised
                ? $"{recognised} Visual Pinball executable{(recognised == 1 ? string.Empty : "s")}"
                : $"{recognised} of {total} programs identified as Visual Pinball";
        }
    }

    public string EvidenceText => Installation.Evidence.Summary;

    [ObservableProperty]
    private bool _isEvidenceVisible;

    public string EvidenceToggleLabel => IsEvidenceVisible ? "Hide details" : "Show details";

    partial void OnIsEvidenceVisibleChanged(bool value) => OnPropertyChanged(nameof(EvidenceToggleLabel));

    public void ToggleEvidence() => IsEvidenceVisible = !IsEvidenceVisible;
}
