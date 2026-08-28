using System.Windows;
using System.Windows.Controls;
using Nudge.App.ViewModels;

namespace Nudge.App.Views;

/// <summary>
/// Code-behind for the setup screen.
///
/// Kept to the one thing that needs a live element - reading which button was clicked. Everything
/// else lives in <see cref="SetupViewModel"/>, which never touches this file.
/// </summary>
public partial class SetupView : UserControl
{
    public SetupView() => InitializeComponent();

    /// <summary>The "Show details" toggle on the Ready screen.</summary>
    private void OnToggleActiveDetails(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupViewModel { Active: { } active })
        {
            active.ToggleEvidence();
        }
    }
}
