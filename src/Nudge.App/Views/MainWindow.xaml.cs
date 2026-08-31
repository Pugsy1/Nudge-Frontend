using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using Nudge.App.ViewModels;

namespace Nudge.App.Views;

public partial class MainWindow : Window
{
    // \u escapes, not literal characters: these are Segoe MDL2 Assets private-use-area glyphs, which
    // don't render in most editors/terminals and have previously been silently dropped or corrupted
    // when written as literal characters (see the maximize/restore glyphs' history).
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>The subset of Win32's MINMAXINFO this actually needs to rewrite - the two fields below are what WM_GETMINMAXINFO uses to decide a maximized window's position and size.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private const int WM_GETMINMAXINFO = 0x0024;

    private bool _isFullscreen;
    private Rect _preFullscreenBounds;

    public MainWindow(ShellViewModel shellViewModel)
    {
        InitializeComponent();

        DataContext = shellViewModel;

        PreviewKeyDown += OnPreviewKeyDown;

        // Fixes a well-known WPF issue: a borderless window using WindowChrome, when maximized,
        // otherwise renders a few pixels past every edge of the monitor (confirmed directly on this
        // window - GetWindowRect returned Left=-7,Top=-7,Right=1927,Bottom=1087 on a 1920x1080
        // screen after clicking the plain Maximize button, no fullscreen involved at all). Windows
        // normally sizes a maximized window from WM_GETMINMAXINFO, which by default answers with the
        // monitor's full bounds rather than its work area (excluding the taskbar) once a window
        // removes its standard non-client border - intercepting the message and supplying the work
        // area ourselves is the standard fix. Must be attached before ToggleFullscreen below ever
        // touches WindowState, so it's registered first in this same SourceInitialized handler.
        SourceInitialized += (_, _) =>
        {
            (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WindowProc);
            ToggleFullscreen();
        };
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            ApplyMaximizedWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>Rewrites the MINMAXINFO Windows is about to size a maximizing window with, so it lands exactly on the current monitor's work area instead of running past its edges.</summary>
    private static void ApplyMaximizedWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        MINMAXINFO minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        // Both expressed relative to the monitor's own top-left, per WM_GETMINMAXINFO's contract.
        minMaxInfo.ptMaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
        minMaxInfo.ptMaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
        minMaxInfo.ptMaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
        minMaxInfo.ptMaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;

        Marshal.StructureToPtr(minMaxInfo, lParam, fDeleteOld: true);
    }

    /// <summary>
    /// Fades the startup splash away. Called by App once startup work has genuinely finished, rather
    /// than from a fixed timeline in the splash's own entry animation - so the loading bar sweeps for
    /// exactly as long as there is real work left. Safe to call more than once: the storyboard simply
    /// re-runs against an already-hidden overlay.
    /// </summary>
    public void DismissSplash()
    {
        if (FindResource("SplashFadeOut") is Storyboard fadeOut)
        {
            // Begun against `this` so Storyboard.TargetName resolves in MainWindow's own name scope,
            // which is where SplashOverlay is declared.
            fadeOut.Begin(this);
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);


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
            // While WindowState is Maximized, Window.Left/Top report the pre-maximize restore
            // position, but ActualWidth/ActualHeight report the current (maximized) rendered size -
            // mixing the two gives a nonsensical rectangle (a windowed position paired with a
            // maximized size) to restore into later. RestoreBounds is WPF's own answer to "what
            // would this window's Normal bounds be right now," valid even while maximized, and is
            // exactly what exiting fullscreen should return to instead (see the WindowState.Normal
            // remarks in the else branch below for why this always exits to Normal, never Maximized).
            _preFullscreenBounds = WindowState == WindowState.Maximized
                ? RestoreBounds
                : new Rect(Left, Top, ActualWidth, ActualHeight);

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

            // Deliberately always Normal, never whatever WindowState was before entering fullscreen.
            // Restoring into WindowState.Maximized here walks straight back into the exact
            // WindowChrome-plus-Maximized bug this method's own remarks describe: the window comes
            // back a few pixels wider/taller than the monitor on every side (observed directly -
            // GetWindowRect returned Left=-7,Top=-7,Right=1927,Bottom=1087 on a 1920x1080 screen
            // instead of 0,0,1920,1080). That only happens via Normal -> Maximize -> fullscreen ->
            // exit fullscreen, which is why it didn't show up until someone actually used the
            // maximize button before toggling fullscreen. The maximize button itself still works
            // fine on its own; this just stops the fullscreen toggle from silently reintroducing the
            // state it exists to avoid.
            WindowState = WindowState.Normal;
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

}
