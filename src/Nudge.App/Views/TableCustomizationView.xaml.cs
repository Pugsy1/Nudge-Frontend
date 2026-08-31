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
    /// Only B, and deliberately only B. This page is reached with Y from the library, so without it
    /// a controller arrives here and is stranded - every other pad-reachable screen (the details
    /// page, the random picker) already backs out, and this one did not.
    ///
    /// Nothing else is bound on purpose. The page is a form of text fields, file pickers and a save
    /// button; there is no meaningful "move between tiles" here for the directions, and mapping A to
    /// Save would let a press the user cannot see the target of commit edits to their table. Getting
    /// out is the part that has to work without a mouse - the rest of the page is honestly a
    /// keyboard-and-mouse screen and is left as one.
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
            if (action is ControllerAction.Back)
            {
                Model?.BackCommand.Execute(null);
            }
        };
        _controller.Start();
    }
}
