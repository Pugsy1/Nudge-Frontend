using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using Nudge.App.ViewModels;

namespace Nudge.App.Views;

public partial class MainWindow : Window
{
    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";
    private const string FullscreenGlyph = "";
    private const string ExitFullscreenGlyph = "";

    private WindowState _preFullscreenState;
    private bool _isFullscreen;

    public MainWindow(ShellViewModel shellViewModel)
    {
        InitializeComponent();

        DataContext = shellViewModel;

        StateChanged += (_, _) => UpdateMaximizeRestoreGlyph();
        PreviewKeyDown += OnPreviewKeyDown;
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

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Maximizes and hides the caption row, rather than manually covering the monitor's full
    /// bounds (which would also cover the taskbar) - simpler, and matches what most windowed apps
    /// mean by a "fullscreen" toggle. The caption row still exists when hidden (Height 0, not
    /// Collapsed), so WindowChrome's CaptionHeight-based hit-testing for drag/resize is unaffected.
    /// </summary>
    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;

        if (_isFullscreen)
        {
            _preFullscreenState = WindowState;
            TitleBarRow.Height = new GridLength(0);
            WindowState = WindowState.Maximized;
        }
        else
        {
            TitleBarRow.Height = (GridLength)FindResource("RowHeight.TitleBar");
            WindowState = _preFullscreenState;
        }

        FullscreenButton.Content = _isFullscreen ? ExitFullscreenGlyph : FullscreenGlyph;
        FullscreenButton.ToolTip = _isFullscreen ? "Exit full screen (F11)" : "Full screen (F11)";
    }

    private void UpdateMaximizeRestoreGlyph()
    {
        bool isMaximized = WindowState == WindowState.Maximized;
        MaximizeRestoreButton.Content = isMaximized ? RestoreGlyph : MaximizeGlyph;
        MaximizeRestoreButton.ToolTip = isMaximized ? "Restore" : "Maximize";
    }
}
