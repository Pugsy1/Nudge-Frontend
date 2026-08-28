using System.Windows;
using Nudge.App.ViewModels;

namespace Nudge.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(SetupViewModel setupViewModel)
    {
        InitializeComponent();

        // The window owns the view model for the one screen Phase 1 has. Navigation between screens
        // arrives with the library shell in Phase 4.
        DataContext = setupViewModel;
    }
}
