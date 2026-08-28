using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nudge.App.ViewModels;

/// <summary>
/// Owns which screen the main window shows: setup (loading / prompt / ready) or the library grid.
/// Phase 1 through 3 had exactly one screen, so <c>MainWindow</c> hosted <c>SetupView</c> directly;
/// this is the first thing to actually switch screens, per Phase 4's "library shell".
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(SetupViewModel setup, LibraryViewModel library)
    {
        Setup = setup;
        Library = library;
        _currentViewModel = setup;

        setup.PropertyChanged += OnSetupPropertyChanged;
    }

    public SetupViewModel Setup { get; }

    public LibraryViewModel Library { get; }

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
            CurrentViewModel = Library;

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
}
