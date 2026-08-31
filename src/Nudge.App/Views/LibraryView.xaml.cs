using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nudge.App.Controls;
using Nudge.App.ViewModels;

namespace Nudge.App.Views;

/// <summary>
/// Code-behind for the library grid. Everything lives in <see cref="ViewModels.LibraryViewModel"/>;
/// the two things that have to happen here are translating a native mouse wheel event into the
/// fractional "tables scrolled" delta the ring layout's continuous scroll position expects, and
/// starting/cancelling each tile's artwork fetch as its container is realized/derealized (a plain
/// data binding has no equivalent of "this item just came on screen").
/// </summary>
public partial class LibraryView : UserControl
{
    private ControllerNavigator? _controller;

    public LibraryView()
    {
        InitializeComponent();

        // Both, because either can happen first: a view built from a DataTemplate normally has its
        // DataContext before Loaded, but relying on that alone means one unlucky ordering leaves the
        // pad silently doing nothing for the whole session.
        Loaded += (_, _) => StartControllerNavigation();
        DataContextChanged += (_, _) => StartControllerNavigation();
        Unloaded += (_, _) =>
        {
            _controller?.Dispose();
            _controller = null;
        };
    }

    /// <summary>
    /// Lets the library be browsed and played with a controller. Only while this view is on screen -
    /// the navigator is created here and disposed when the view goes away, so a pad is never driving
    /// a screen the user is not looking at.
    /// </summary>
    private void StartControllerNavigation()
    {
        if (_controller is not null || DataContext is not LibraryViewModel library)
        {
            return;
        }

        _controller = new ControllerNavigator(library.ControllerReader);
        _controller.Action += OnControllerAction;
        _controller.Start();

        library.SelectionMoved += OnSelectionMoved;

        // Real movement only - WPF raises MouseMove for all sorts of reasons while the pointer sits
        // still (layout changes under it, scrolling), and any of those would kick the UI straight
        // back out of controller mode mid-navigation.
        PreviewMouseMove += (_, e) =>
        {
            Point position = e.GetPosition(this);
            if (_lastMousePosition is { } last && (position - last).Length < 2)
            {
                return;
            }

            _lastMousePosition = position;
            library.ExitControllerMode();
        };
    }

    private Point? _lastMousePosition;

    private void OnControllerAction(ControllerAction action)
    {
        if (DataContext is not LibraryViewModel library)
        {
            return;
        }

        // Any pad input switches the UI into controller mode and guarantees a selection exists, so
        // the very first press does something visible instead of appearing to be ignored.
        library.EnterControllerMode();

        // The random picker is modal, so it owns the pad while it is up - otherwise the directions
        // would be scrolling the library invisibly behind it.
        if (library.IsRandomPickerOpen)
        {
            switch (action)
            {
                case ControllerAction.Activate:
                    library.PlayRandomTableCommand.Execute(null);
                    break;
                case ControllerAction.Details:
                    library.OpenRandomTableDetailsCommand.Execute(null);
                    break;
                case ControllerAction.Left:
                case ControllerAction.Right:
                    library.PickRandomTableCommand.Execute(null);
                    break;
                case ControllerAction.Back:
                    library.CloseRandomPickerCommand.Execute(null);
                    break;
            }

            return;
        }

        // The keyboard is modal while open: it is a grid of its own, and letting the library also
        // read the directions underneath would move both at once.
        if (library.IsOnScreenKeyboardOpen && library.OnScreenKeyboard is { } keyboard)
        {
            switch (action)
            {
                case ControllerAction.Up: keyboard.Move(0, -1); break;
                case ControllerAction.Down: keyboard.Move(0, 1); break;
                case ControllerAction.Left: keyboard.Move(-1, 0); break;
                case ControllerAction.Right: keyboard.Move(1, 0); break;
                case ControllerAction.Activate: keyboard.TypeSelected(); break;
                case ControllerAction.Details: keyboard.Backspace(); break;
                case ControllerAction.Favorite: keyboard.Clear(); break;
                case ControllerAction.Back:
                case ControllerAction.Menu:
                    library.CloseOnScreenKeyboard();
                    break;
            }

            return;
        }

        // A slider being adjusted owns the pad completely until it is confirmed - see EnterHeader's
        // remarks for why a slider needs a mode of its own rather than reacting to bare directions.
        if (_adjustingSlider is { } slider)
        {
            switch (action)
            {
                case ControllerAction.Left:
                    slider.Value = Math.Max(slider.Minimum, slider.Value - slider.SmallChange);
                    break;
                case ControllerAction.Right:
                    slider.Value = Math.Min(slider.Maximum, slider.Value + slider.SmallChange);
                    break;
                case ControllerAction.Activate:
                case ControllerAction.Back:
                    _adjustingSlider = null;
                    break;
            }

            return;
        }

        if (_inHeader)
        {
            HandleHeaderAction(library, action);
            return;
        }

        TableTileViewModel? selected = library.SelectedTile;

        switch (action)
        {
            case ControllerAction.Up:
                // Already on the first row, so up leaves the grid entirely and lands in the header.
                if (library.IsSelectionOnTopRow)
                {
                    EnterHeader(library);
                    break;
                }

                library.MoveSelection(0, -1);
                break;
            case ControllerAction.Down:
                library.MoveSelection(0, 1);
                break;
            case ControllerAction.Left:
                library.MoveSelection(-1, 0);
                break;
            case ControllerAction.Right:
                library.MoveSelection(1, 0);
                break;

            case ControllerAction.Activate when selected is not null:
                library.LaunchTableCommand.Execute(selected);
                break;
            case ControllerAction.Details when selected is not null:
                library.OpenTableDetailsCommand.Execute(selected);
                break;
            case ControllerAction.Customize when selected is not null:
                library.OpenTableCustomizationCommand.Execute(selected);
                break;
            case ControllerAction.Favorite when selected is not null:
                library.ToggleFavoriteCommand.Execute(selected);
                break;

            case ControllerAction.Menu:
                library.ToggleSettingsCommand.Execute(null);
                break;

            // Back is deliberately unhandled here. This is the root screen, so there is nowhere to
            // go; the tempting alternative - dropping out of controller mode - would take the
            // selection highlight away and read as the button having broken something.
            case ControllerAction.Back:
                break;
        }
    }

    private bool _inHeader;
    private Slider? _adjustingSlider;

    /// <summary>
    /// Moves focus out of the tile grid and into the header controls - the 2D/VR switch, search,
    /// sort, layout and the density slider. The grid keeps its selection while up here, so dropping
    /// back down returns to the tile you left rather than to the start.
    /// </summary>
    private void EnterHeader(LibraryViewModel library)
    {
        _inHeader = true;
        library.IsHeaderFocused = true;
        HeaderBar.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void LeaveHeader(LibraryViewModel library)
    {
        _inHeader = false;
        _adjustingSlider = null;
        library.IsHeaderFocused = false;
        Keyboard.ClearFocus();
    }

    private void HandleHeaderAction(LibraryViewModel library, ControllerAction action)
    {
        switch (action)
        {
            case ControllerAction.Left:
                MoveWithinHeader(FocusNavigationDirection.Previous);
                break;
            case ControllerAction.Right:
                MoveWithinHeader(FocusNavigationDirection.Next);
                break;

            case ControllerAction.Down:
            case ControllerAction.Back:
                LeaveHeader(library);
                break;

            case ControllerAction.Activate:
                ActivateHeaderControl(library);
                break;

            case ControllerAction.Menu:
                library.ToggleSettingsCommand.Execute(null);
                break;
        }
    }

    private void MoveWithinHeader(FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is UIElement current)
        {
            current.MoveFocus(new TraversalRequest(direction));
        }
        else
        {
            HeaderBar.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }
    }

    /// <summary>
    /// Presses the focused header control. A slider is the one control that cannot simply be
    /// "pressed": it holds a value rather than performing an action, and letting bare left/right
    /// change it while merely passing over would make moving through the header silently alter the
    /// grid density. So A enters an explicit adjust mode, left/right change the value, and A (or B)
    /// confirms - the value is live throughout, so there is nothing to commit, only a mode to leave.
    /// </summary>
    private void ActivateHeaderControl(LibraryViewModel library)
    {
        if (Keyboard.FocusedElement is Slider slider)
        {
            _adjustingSlider = slider;
            return;
        }

        if (Keyboard.FocusedElement is TextBox)
        {
            // Text needs characters, which a pad has no way to produce - the on-screen keyboard is
            // what makes the search box usable without reaching for a keyboard.
            library.OpenOnScreenKeyboard();
            return;
        }

        FormControllerNavigation.ActivateFocused();
    }

    /// <summary>
    /// Scrolls the newly selected tile into view. Uses the realized container when there is one and
    /// falls back to scrolling the list proportionally when there is not - with virtualization the
    /// tile being moved onto very often does not exist yet, which is precisely the case that has to
    /// work for the selection to keep travelling past the bottom of the screen.
    /// </summary>
    private void OnSelectionMoved(TableTileViewModel tile)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            foreach (ItemsControl host in new[] { GridTiles, CompactTiles, ListTiles })
            {
                if (host is null || host.Visibility != Visibility.Visible)
                {
                    continue;
                }

                if (host.ItemContainerGenerator.ContainerFromItem(tile) is FrameworkElement container)
                {
                    container.BringIntoView();
                    return;
                }

                // Not realized: scroll to roughly where the item sits, then let the next layout pass
                // bring the now-realized container fully into view.
                if (FindScrollViewer(host) is { } scroller && host.Items.Count > 0)
                {
                    int index = host.Items.IndexOf(tile);
                    if (index >= 0)
                    {
                        double fraction = (double)index / host.Items.Count;
                        scroller.ScrollToVerticalOffset(fraction * scroller.ExtentHeight);
                    }
                }

                return;
            }
        }));
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject start)
    {
        for (DependencyObject? current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollViewer viewer)
            {
                return viewer;
            }
        }

        return null;
    }

    /// <summary>Fires when a tile's container (grid or ring, both templates wire this up) is realized - the earliest point it's worth asking IArtworkProvider for that table's artwork.</summary>
    private void OnTileLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TableTileViewModel tile })
        {
            tile.BeginLoadArtwork();
        }
    }

    /// <summary>Fires when a tile's container is recycled/derealized - cancels a still-in-flight fetch for a tile that scrolled back out of view before it resolved.</summary>
    private void OnTileUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TableTileViewModel tile })
        {
            tile.CancelLoadArtwork();
        }
    }

    /// <summary>
    /// Fires when the pointer enters a tile's card (grid and carousel both wire this up through the
    /// shared TableCard style) - starts playing that table's hover video, if it has one and the
    /// "Play video on hover" setting is on. The MediaElement lives inside TableCard's own
    /// ControlTemplate rather than the DataTemplate, so it has to be reached through the button's
    /// applied template rather than a plain named-element lookup. Play() is called immediately, but
    /// the fade-in (OnTrailerMediaOpened) waits for the first real frame to actually be ready - it
    /// starts at Opacity 0 in the template, so without this it would still show a hard pop, just of
    /// a black/blank frame instead of the video.
    /// </summary>
    private void OnTileMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Button { DataContext: TableTileViewModel tile } button
            || DataContext is not LibraryViewModel library
            || !library.EnableMediaTrailers)
        {
            return;
        }

        // Asks the view model rather than reading tile.VideoPath directly: most tables have no
        // hand-assigned video, and this is what finds the one already sitting in the user's media
        // folder. Returns null (cached) for tables that genuinely have none.
        string? videoPath = library.ResolveHoverVideo(tile);
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            // No local file, and deliberately no online fallback. Online previews used to play here
            // through an embedded browser, which is a native child window: it swallowed the mouse
            // wheel, so scrolling the library died the moment the pointer crossed a tile that had a
            // trailer. Online video now lives on the details page, where a native player is exactly
            // what is wanted and nothing needs to scroll past it.
            return;
        }

        if (button.Template?.FindName("TrailerPlayer", button) is not MediaElement player)
        {
            return;
        }

        player.Visibility = Visibility.Visible;
        player.Opacity = 0;
        player.IsMuted = library.MuteMediaTrailers;
        player.MediaOpened -= OnTrailerMediaOpened;
        player.MediaOpened += OnTrailerMediaOpened;
        player.Source = new Uri(videoPath);
        player.Play();
    }

    /// <summary>Eases the hover video in once it actually has a frame to show, rather than the instant it's told to play - see OnTileMouseEnter's remarks.</summary>
    private static void OnTrailerMediaOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement player)
        {
            return;
        }

        player.MediaOpened -= OnTrailerMediaOpened;

        // Fade paired with a slight settle inwards, rather than opacity alone: the frame starts a
        // touch larger than the tile and eases down to fit, so the preview reads as opening into the
        // artwork it's replacing instead of a video being cross-faded on top of it. The transform is
        // created here rather than declared in the template because it has to be animated by
        // reference - a Storyboard inside the ControlTemplate cannot resolve a target name from the
        // code-behind that starts it.
        var settle = new ScaleTransform(1.06, 1.06);
        player.RenderTransformOrigin = new Point(0.5, 0.5);
        player.RenderTransform = settle;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var scaleTo = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease };
        settle.BeginAnimation(ScaleTransform.ScaleXProperty, scaleTo);
        settle.BeginAnimation(ScaleTransform.ScaleYProperty, scaleTo);

        player.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
    }

    /// <summary>Fires when the pointer leaves a tile's card - stops and unloads the hover video so it never keeps playing (or holding a file handle) once the table isn't in view.</summary>
    private void OnTileMouseLeave(object sender, MouseEventArgs e)
    {
        // Deliberately does NOT hide the online preview. That overlay is a native window which takes
        // mouse input away from the tile, so this fires the instant it appears - hiding here meant
        // show, cover, leave, hide, re-enter, show, in a loop. OnPreviewWatchTick owns dismissing it
        // instead, driven by where the cursor actually is.
        if (sender is not Button button)
        {
            return;
        }

        if (button.Template?.FindName("TrailerPlayer", button) is not MediaElement player)
        {
            return;
        }

        player.MediaOpened -= OnTrailerMediaOpened;
        player.BeginAnimation(UIElement.OpacityProperty, null);
        player.Opacity = 0;

        // Clear the settle transform too, not just the opacity animation: a container recycled by
        // the virtualizing panel would otherwise start its next hover already scaled, and the
        // animation would run from 1.06 to 1.0 against a frame that never left 1.0.
        player.RenderTransform = null;

        player.Stop();
        player.Visibility = Visibility.Collapsed;
        player.Source = null;
    }

    /// <summary>
    /// Mouse wheel over the ring scrubs its scroll position directly - not a fixed "one table per
    /// notch" step, so a precision trackpad's finer wheel events (smaller |e.Delta| than a standard
    /// notch's 120) move the ring by a proportionally smaller fraction of a table, which is what
    /// actually gives the smooth scrubbing feel (LibraryViewModel.CarouselPosition's remarks). Sign is
    /// inverted from raw wheel delta so scrolling up moves toward earlier tables, matching the grid's
    /// own vertical scroll direction.
    /// </summary>
    private void OnCarouselMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        double steps = e.Delta / 120.0;
        viewModel.CarouselScrollCommand.Execute(-steps);
    }
}
