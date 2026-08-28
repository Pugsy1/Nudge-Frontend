using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using Nudge.App.ViewModels;

namespace Nudge.App.Views;

public partial class MainWindow : Window
{
    // \u escapes, not literal characters: these are Segoe MDL2 Assets private-use-area glyphs, which
    // don't render in most editors/terminals and have previously been silently dropped or corrupted
    // when written as literal characters (see the maximize/restore glyphs' history).
    private const string MaximizeGlyph = "\uE922";
    private const string RestoreGlyph = "\uE923";
    private const string FullscreenGlyph = "\uE740";
    private const string ExitFullscreenGlyph = "\uE73F";

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private bool _isFullscreen;
    private WindowState _preFullscreenState;
    private Rect _preFullscreenBounds;

    public MainWindow(ShellViewModel shellViewModel)
    {
        InitializeComponent();

        DataContext = shellViewModel;

        StateChanged += (_, _) => UpdateMaximizeRestoreGlyph();
        PreviewKeyDown += OnPreviewKeyDown;
        UpdateMaximizeRestoreGlyph();

        // SourceInitialized, not Loaded: the Win32 HWND (and so PresentationSource/CompositionTarget,
        // which ApplyMonitorBounds needs for the DPI conversion) exists by this point, but the window
        // hasn't been shown yet - so it opens directly at fullscreen bounds instead of flashing the
        // windowed size first and then snapping to fullscreen a frame later.
        SourceInitialized += (_, _) => ToggleFullscreen();
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
    /// Genuine fullscreen: covers the whole monitor the window is currently on, taskbar included,
    /// rather than <see cref="WindowState.Maximized"/> (which only fills the work area, and - under
    /// WindowChrome specifically - kept fighting with the caption hit-testing region when toggled
    /// together with hiding the title row, which is what made the first version of this "not quite
    /// work right"). Explicit monitor bounds via Win32 sidestep that entirely: the window is just
    /// told to be exactly as big as the screen, on whichever monitor it's actually on, independent
    /// of WindowState.
    /// </summary>
    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;

        WindowChrome? chrome = WindowChrome.GetWindowChrome(this);

        if (_isFullscreen)
        {
            _preFullscreenState = WindowState;
            _preFullscreenBounds = new Rect(Left, Top, ActualWidth, ActualHeight);

            // Manual bounds only apply predictably from Normal - Maximized ignores Left/Top/Width/Height.
            if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
            }

            TitleBarRow.Height = new GridLength(0);
            if (chrome is not null)
            {
                chrome.CaptionHeight = 0;
            }

            ApplyMonitorBounds();
        }
        else
        {
            TitleBarRow.Height = (GridLength)FindResource("RowHeight.TitleBar");
            if (chrome is not null)
            {
                chrome.CaptionHeight = (double)FindResource("Size.TitleBar.Height");
            }

            Left = _preFullscreenBounds.Left;
            Top = _preFullscreenBounds.Top;
            Width = _preFullscreenBounds.Width;
            Height = _preFullscreenBounds.Height;
            WindowState = _preFullscreenState;
        }

        FullscreenButton.Content = _isFullscreen ? ExitFullscreenGlyph : FullscreenGlyph;
        FullscreenButton.ToolTip = _isFullscreen ? "Exit full screen (F11)" : "Full screen (F11)";

        // The real title bar row is Height 0 while fullscreen, so this corner pair (exit
        // fullscreen + close) is the only chrome available without reaching for F11/Alt+F4.
        FullscreenCornerControls.Visibility = _isFullscreen ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Sets Left/Top/Width/Height to exactly cover the current monitor, converting the Win32 device-pixel bounds to WPF's device-independent units so this is correct at any display scaling.</summary>
    private void ApplyMonitorBounds()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        System.Windows.Media.Matrix transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                                                 ?? System.Windows.Media.Matrix.Identity;

        Left = info.rcMonitor.Left * transform.M11;
        Top = info.rcMonitor.Top * transform.M22;
        Width = (info.rcMonitor.Right - info.rcMonitor.Left) * transform.M11;
        Height = (info.rcMonitor.Bottom - info.rcMonitor.Top) * transform.M22;
    }

    private void UpdateMaximizeRestoreGlyph()
    {
        bool isMaximized = WindowState == WindowState.Maximized;
        MaximizeRestoreButton.Content = isMaximized ? RestoreGlyph : MaximizeGlyph;
        MaximizeRestoreButton.ToolTip = isMaximized ? "Restore" : "Maximize";
    }
}
