using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nudge.App.ViewModels;

/// <summary>
/// Owns which screen the main window shows: setup (loading / prompt / ready), the library grid, or
/// settings. Phase 1 through 3 had exactly one screen, so <c>MainWindow</c> hosted <c>SetupView</c>
/// directly; this is the first thing to actually switch screens, per Phase 4's "library shell".
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(SetupViewModel setup, LibraryViewModel library, SettingsViewModel settings)
    {
        Setup = setup;
        Library = library;
        Settings = settings;
        _currentViewModel = setup;

        setup.PropertyChanged += OnSetupPropertyChanged;
        library.PropertyChanged += OnLibraryPropertyChanged;
    }

    public SetupViewModel Setup { get; }

    public LibraryViewModel Library { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private object _currentViewModel;

    public Task InitialiseAsync() => Setup.InitialiseAsync();

    private void OnSetupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SetupViewModel.Stage))
        {
            return;
        }

        if (Setup.Stage == SetupStage.Ready && Setup.Active is not null)
        {
            CurrentViewModel = Library.IsSettingsOpen ? Settings : Library;

            // Fire-and-forget from the UI's perspective: LibraryViewModel reports its own progress
            // and errors through its own observable properties, the same pattern SetupViewModel
            // already uses for BrowseAsync.
            _ = Library.ActivateAsync(Setup.Active.Installation);
        }
        else
        {
            CurrentViewModel = Setup;
        }
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        bool relevant = e.PropertyName is nameof(LibraryViewModel.IsSettingsOpen)
                         or nameof(LibraryViewModel.EditingTableViewModel)
                         or nameof(LibraryViewModel.DetailsTableViewModel);
        if (!relevant || Setup.Stage != SetupStage.Ready)
        {
            return;
        }

        // EditingTableViewModel takes priority over IsSettingsOpen - the customization page opens
        // from the library, not from Settings, so there's no real case where both are meant to be
        // true at once, but if it ever happens, showing the more specific/recently-opened page wins.
        // Customization before details before settings: each is opened from the one before it, so
        // the most specific page that is currently open is always the one meant to be on screen.
        CurrentViewModel = Library.EditingTableViewModel is not null
            ? Library.EditingTableViewModel
            : Library.DetailsTableViewModel is not null
                ? Library.DetailsTableViewModel
                : Library.IsSettingsOpen ? Settings : Library;
    }
}
