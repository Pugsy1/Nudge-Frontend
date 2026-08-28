using System.Windows;
using System.Windows.Shell;
using Nudge.App.ViewModels;

namespace Nudge.App.Views;

public partial class MainWindow : Window
{
    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";

    public MainWindow(ShellViewModel shellViewModel)
    {
        InitializeComponent();

        DataContext = shellViewModel;

        StateChanged += (_, _) => UpdateMaximizeRestoreGlyph();
        UpdateMaximizeRestoreGlyph();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void UpdateMaximizeRestoreGlyph()
    {
        bool isMaximized = WindowState == WindowState.Maximized;
        MaximizeRestoreButton.Content = isMaximized ? RestoreGlyph : MaximizeGlyph;
        MaximizeRestoreButton.ToolTip = isMaximized ? "Restore" : "Maximize";
    }
}
