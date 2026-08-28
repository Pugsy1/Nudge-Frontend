using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nudge.App.Services;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.App.ViewModels;

/// <summary>Which of the three screens the setup flow is currently showing.</summary>
public enum SetupStage
{
    /// <summary>Checking whether a previously confirmed installation is still there.</summary>
    Loading,

    /// <summary>Nothing confirmed yet: "Where is Visual Pinball installed?"</summary>
    Prompt,

    /// <summary>An installation is confirmed. This is as far as Phase 1 goes.</summary>
    Ready
}

/// <summary>
/// The setup screen. Deliberately a single, simple choice: point Nudge at a Visual Pinball folder.
/// Picking a folder confirms it immediately - there is no separate confirm step - and Nudge never
/// writes to, moves, or deletes anything inside that folder; it only ever reads it.
/// </summary>
/// <remarks>
/// Nudge never guesses a folder on the user's behalf. It does not scan the machine and offer a
/// suggestion, and Browse never pre-seeds a starting directory. The only folder Nudge will use
/// without the user picking it in the current session is one they explicitly chose on a previous
/// run - that is remembering an instruction the user already gave, not assuming one.
///
/// This class never touches the filesystem, the registry or a dialog directly. Everything it does
/// goes through an injected service, which is what keeps it testable and keeps the UI thread free.
/// </remarks>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly IVpxInstallationDiscovery _discovery;
    private readonly ISettingsService _settingsService;
    private readonly IFolderPickerService _folderPicker;
    private readonly IThemeService _themeService;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<SetupViewModel> _logger;

    public SetupViewModel(
        IVpxInstallationDiscovery discovery,
        ISettingsService settingsService,
        IFolderPickerService folderPicker,
        IThemeService themeService,
        IPathRedactor redactor,
        ILogger<SetupViewModel> logger)
    {
        _discovery = discovery;
        _settingsService = settingsService;
        _folderPicker = folderPicker;
        _themeService = themeService;
        _redactor = redactor;
        _logger = logger;
    }

    [ObservableProperty]
    private SetupStage _stage = SetupStage.Loading;

    /// <summary>The installation Nudge is set up to use. Set once the user picks one via Browse.</summary>
    [ObservableProperty]
    private InstallationViewModel? _active;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Browse to the folder that contains VPinballX.exe.";

    /// <summary>Set when a folder the user picked did not work out. Cleared on the next attempt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private string _settingsFilePath = string.Empty;

    public bool IsIdle => !IsBusy;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ThemeToggleLabel => _themeService.Current == AppTheme.Dark
        ? "Switch to light theme"
        : "Switch to dark theme";

    /// <summary>
    /// Runs once, after the window is on screen. If the user explicitly confirmed a folder on a
    /// previous run and it still checks out, Nudge goes straight to the Ready screen. Otherwise it
    /// shows the Prompt screen with nothing pre-filled - Nudge does not scan the machine or guess.
    /// </summary>
    public async Task InitialiseAsync()
    {
        SettingsFilePath = _settingsService.SettingsFilePath;
        Stage = SetupStage.Loading;

        try
        {
            NudgeSettings settings = await _settingsService.LoadAsync().ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(settings.SelectedInstallationPath))
            {
                Result<VpxInstallation> revalidated = await _discovery
                    .InspectFolderAsync(settings.SelectedInstallationPath)
                    .ConfigureAwait(true);

                if (revalidated.IsSuccess)
                {
                    Active = new InstallationViewModel(revalidated.Value);
                    Stage = SetupStage.Ready;
                    _logger.LogInformation(
                        "Previously confirmed installation at {Path} still checks out; skipping setup.",
                        _redactor.Redact(settings.SelectedInstallationPath));
                    return;
                }

                _logger.LogInformation(
                    "Previously confirmed installation at {Path} no longer checks out: {Reason}",
                    _redactor.Redact(settings.SelectedInstallationPath),
                    revalidated.Error);
                StatusMessage = "Nudge can't find Visual Pinball where you last set it up. "
                                 + "Browse to the folder again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Checking the previously confirmed installation failed.");
        }

        Stage = SetupStage.Prompt;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task BrowseAsync()
    {
        // No initial directory is passed here on purpose. Nudge does not assume where the user's
        // Visual Pinball folder is - Browse always opens wherever Windows itself last left the
        // folder picker, so the user picks freely.
        string? chosen = _folderPicker.PickFolder("Choose the folder that contains VPinballX.exe");

        if (string.IsNullOrWhiteSpace(chosen))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            Result<VpxInstallation> result = await _discovery.InspectFolderAsync(chosen).ConfigureAwait(true);

            if (result.IsFailure)
            {
                // A folder that is not a Visual Pinball install is an ordinary thing for a user to
                // pick. It gets a plain explanation, not a crash and not a stack trace.
                ErrorMessage = result.Error;
                return;
            }

            await ConfirmAsync(result.Value).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inspecting the chosen folder {Folder} failed.", _redactor.Redact(chosen));
            ErrorMessage = "Nudge could not read that folder. The log file has the details.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Saves the chosen installation and moves straight to the Ready screen. Nudge only reads this
    /// folder to get here - nothing inside it is changed, moved, or deleted.
    /// </summary>
    private async Task ConfirmAsync(VpxInstallation installation)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            NudgeSettings settings = await _settingsService.LoadAsync().ConfigureAwait(true);

            settings.SelectedInstallationId = installation.Id;
            settings.SelectedInstallationPath = installation.RootPath;

            KnownInstallation? known = settings.KnownInstallations
                .FirstOrDefault(k => string.Equals(k.Id, installation.Id, StringComparison.Ordinal));

            if (known is null)
            {
                settings.KnownInstallations.Add(new KnownInstallation
                {
                    Id = installation.Id,
                    RootPath = installation.RootPath,
                    DisplayName = installation.DisplayName,
                    DateAdded = DateTimeOffset.Now,
                    IsDefault = true
                });
            }
            else
            {
                known.RootPath = installation.RootPath;
                known.DisplayName = installation.DisplayName;
            }

            foreach (KnownInstallation entry in settings.KnownInstallations)
            {
                entry.IsDefault = string.Equals(entry.Id, installation.Id, StringComparison.Ordinal);
            }

            await _settingsService.SaveAsync(settings).ConfigureAwait(true);

            Active = new InstallationViewModel(installation);
            Stage = SetupStage.Ready;

            _logger.LogInformation(
                "Confirmed installation {Id} at {Path}.",
                installation.Id,
                _redactor.Redact(installation.RootPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving the chosen installation failed.");
            ErrorMessage = "Nudge could not save your choice. The log file has the details.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Back to the Prompt screen, e.g. because the user wants to point Nudge somewhere else.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ChangeFolder()
    {
        ErrorMessage = null;
        StatusMessage = "Browse to the folder that contains VPinballX.exe.";
        Stage = SetupStage.Prompt;
    }

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        AppTheme next = _themeService.Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _themeService.Apply(next);
        OnPropertyChanged(nameof(ThemeToggleLabel));

        try
        {
            NudgeSettings settings = await _settingsService.LoadAsync().ConfigureAwait(true);
            settings.ThemeName = next.ToString();
            await _settingsService.SaveAsync(settings).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The theme already changed on screen; failing to remember it is not worth an error box.
            _logger.LogWarning(ex, "Could not save the theme preference.");
        }
    }
}
