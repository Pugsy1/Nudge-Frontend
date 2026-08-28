using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nudge.App.ViewModels;

/// <summary>
/// The settings screen. A full page rather than a popup, per explicit request, since this is meant
/// to grow: favourites, playtime tracking, and artwork source all land here once the data behind
/// them exists (see the "not built here" note on <see cref="LibraryViewModel"/>). Deliberately
/// holds no state of its own - every preference already lives on <see cref="LibraryViewModel"/>,
/// this just presents it with more room than a flyout had.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(LibraryViewModel library)
    {
        Library = library;
    }

    public LibraryViewModel Library { get; }

    [RelayCommand]
    private void Back() => Library.IsSettingsOpen = false;
}
