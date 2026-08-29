using System.Windows.Controls;
using System.Windows.Input;

namespace Nudge.App.Views;

/// <summary>Code-behind for the settings page. Everything lives in <see cref="ViewModels.SettingsViewModel"/>; nothing here needs a live element.</summary>
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
        Loaded += (_, _) => Keyboard.ClearFocus();
    }
}
