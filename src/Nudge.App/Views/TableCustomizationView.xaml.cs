using System.Windows.Controls;
using Nudge.App.Controls;
using Nudge.App.ViewModels;

namespace Nudge.App.Views;

/// <summary>
/// Code-behind for the per-table customization page. Everything lives in
/// <see cref="ViewModels.TableCustomizationViewModel"/>; the only thing that cannot be expressed in
/// XAML is letting a controller back out of the page.
/// </summary>
public partial class TableCustomizationView : UserControl
{
    private ControllerNavigator? _controller;

    public TableCustomizationView()
    {
        InitializeComponent();

        // Both, because either can happen first - same reasoning as LibraryView's own wiring.
        Loaded += (_, _) => StartControllerNavigation();
        DataContextChanged += (_, _) => StartControllerNavigation();
        Unloaded += (_, _) =>
        {
            _controller?.Dispose();
            _controller = null;
        };
    }

    private TableCustomizationViewModel? Model => DataContext as TableCustomizationViewModel;

    /// <summary>
    /// Full navigation, not just an escape hatch. Originally only B was bound, on the reasoning that
    /// A activating an unseen control could commit edits by accident - but that reasoning depended
    /// on there being no visible focus. Moving focus properly means the control being activated is
    /// the one outlined on screen, which is no more dangerous than clicking it.
    ///
    /// Directions move between controls, A presses whatever has focus, B leaves. Text fields still
    /// need a keyboard to type into; the pad gets you to them and out again.
    /// </summary>
    private void StartControllerNavigation()
    {
        if (_controller is not null || Model?.Library is not { } library)
        {
            return;
        }

        _controller = new ControllerNavigator(library.ControllerReader);
        _controller.Action += action =>
        {
            library.EnterControllerMode();

            if (FormControllerNavigation.Apply(this, action))
            {
                return;
            }

            if (action is ControllerAction.Back)
            {
                Model?.BackCommand.Execute(null);
            }
        };
        _controller.Start();
    }
}
