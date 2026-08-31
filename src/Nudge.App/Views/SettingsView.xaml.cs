using System.Windows.Controls;
using Nudge.App.Controls;
using System.Windows.Input;
using Nudge.App.ViewModels;

namespace Nudge.App.Views;

/// <summary>
/// Code-behind for the settings page. Almost everything lives in
/// <see cref="ViewModels.SettingsViewModel"/>; what has to happen here is the controller rebinding
/// flow, which needs a live keyboard event that no binding can express.
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // Without this, WPF hands keyboard focus to the first focusable control on this page the
        // instant it appears - the back button, which was still holding focus from a Tab/click on
        // the settings cog that opened this page. That flashes its IsKeyboardFocused accent border
        // for a frame even though nobody touched the back button. Making the button itself
        // unfocusable stopped focus from landing there specifically, but WPF just picks the next
        // focusable control on the page instead (the theme ComboBox) and flashes its own focus
        // visual the same way - clearing focus outright, rather than only relocating it, is the
        // only way nothing on the page shows a focus state it didn't earn.
        Loaded += (_, _) =>
        {
            Keyboard.ClearFocus();
            Library?.BeginControllerSetup();
        };

        // Polling a controller sixty times a second is only worth doing while these rows are on
        // screen, so it stops the moment the page goes away.
        Unloaded += (_, _) => Library?.EndControllerSetup();

        // PreviewKeyDown, not KeyDown: a rebind has to be able to capture keys the rest of the UI
        // would otherwise swallow first (Tab moves focus, Space and Enter activate the focused
        // button, arrows scroll). Previewing lets the listening row claim the press before any of
        // that happens, and everything is passed straight through whenever nothing is listening.
        PreviewKeyDown += OnPreviewKeyDown;

        // Settings is reachable with Start from the library, so it needs a way back out that does
        // not involve a mouse. Only B is bound: this is a page of toggles and text fields, and the
        // rebinding rows in particular are listening for real key presses - having the pad also
        // drive the page would collide with exactly that.
        Loaded += (_, _) => StartControllerNavigation();
        Unloaded += (_, _) =>
        {
            _controller?.Dispose();
            _controller = null;
        };
    }

    private ControllerNavigator? _controller;
    private FormControllerNavigation? _formNavField;

    /// <summary>One navigator per page, created lazily - it remembers the last focused control, which is what stops navigation resetting to the top.</summary>
    private FormControllerNavigation _formNav => _formNavField ??= new FormControllerNavigation(this);

    private void StartControllerNavigation()
    {
        if (_controller is not null || Library is not { } library)
        {
            return;
        }

        _controller = new ControllerNavigator(library.ControllerReader);
        _controller.Action += action =>
        {
            library.EnterControllerMode();

            if (action is ControllerAction.Back or ControllerAction.Menu)
            {
                library.IsSettingsOpen = false;
                return;
            }

            _formNav.Apply(action);
        };
        _controller.Start();
    }

    private LibraryViewModel? Library => (DataContext as SettingsViewModel)?.Library;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        LibraryViewModel? library = Library;
        if (library is null)
        {
            return;
        }

        // System keys (Alt combinations) arrive as Key.System with the real key in SystemKey.
        Key pressed = e.Key == Key.System ? e.SystemKey : e.Key;

        if (library.ApplyCapturedKey(pressed))
        {
            e.Handled = true;
        }
    }
}
