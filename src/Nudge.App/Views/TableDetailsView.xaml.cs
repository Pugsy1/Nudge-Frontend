using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nudge.App.Controls;
using Nudge.App.ViewModels;

namespace Nudge.App.Views;

/// <summary>
/// Code-behind for the table details page. The only thing that cannot be expressed in XAML is the
/// embedded video player: it is a native child window, so it has to be positioned over the page by
/// hand rather than placed in the layout, and torn down when the page goes away.
/// </summary>
public partial class TableDetailsView : UserControl
{
    private TrailerOverlay? _player;

    public TableDetailsView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Hook();
            StartControllerNavigation();
        };
        Unloaded += (_, _) =>
        {
            Unhook();

            _controller?.Dispose();
            _controller = null;

            // Explicitly, not just on collapse: leaving the page while a video is playing must stop
            // it, or the audio (once unmuted by the viewer) keeps going over the library.
            _player?.Hide();
        };

        // The slot moves as the page scrolls and as content above it resizes, so the player has to
        // follow rather than being placed once.
        PlayerSlot.LayoutUpdated += (_, _) => PositionPlayer();
    }

    private TableDetailsViewModel? Model => DataContext as TableDetailsViewModel;

    private ControllerNavigator? _controller;

    /// <summary>
    /// Controller support on this page too, so arriving here with a pad isn't a dead end. Only the
    /// handful of actions the page actually offers are bound; directions are ignored, since there is
    /// nothing to move between.
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
            switch (action)
            {
                case ControllerAction.Activate:
                    Model?.PlayCommand.Execute(null);
                    break;
                case ControllerAction.Customize:
                    Model?.CustomizeCommand.Execute(null);
                    break;
                case ControllerAction.Back:
                    Model?.BackCommand.Execute(null);
                    break;
            }
        };
        _controller.Start();
    }

    private void Hook()
    {
        if (Model is { } model)
        {
            model.PropertyChanged += OnModelPropertyChanged;
            PositionPlayer();
        }
    }

    private void Unhook()
    {
        if (Model is { } model)
        {
            model.PropertyChanged -= OnModelPropertyChanged;
        }
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TableDetailsViewModel.TrailerVideoId)
            or nameof(TableDetailsViewModel.HasTrailer))
        {
            PositionPlayer();
        }
    }

    /// <summary>
    /// Places the player over the slot reserved for it in the layout. Called on every layout pass
    /// because the page scrolls: a native window is not part of the visual tree and does not move
    /// with the content it appears to sit in, so its position has to be recomputed continuously or
    /// it stays put while the page scrolls underneath it.
    /// </summary>
    private void PositionPlayer()
    {
        if (Model is not { HasTrailer: true, TrailerVideoId: { } videoId })
        {
            _player?.Hide();
            return;
        }

        if (PlayerSlot.ActualWidth <= 0 || PlayerSlot.ActualHeight <= 0)
        {
            return;
        }

        try
        {
            GeneralTransform transform = PlayerSlot.TransformToVisual(PlayerHost);
            Rect bounds = transform.TransformBounds(new Rect(0, 0, PlayerSlot.ActualWidth, PlayerSlot.ActualHeight));

            // Scrolled out of view - hidden rather than left floating over the header, which is
            // exactly what a native window does if nobody moves it.
            if (bounds.Bottom < 0 || bounds.Top > PlayerHost.ActualHeight)
            {
                _player?.Hide();
                return;
            }

            _player ??= new TrailerOverlay(PlayerHost);

            string background = ToCssColor(TryFindResource("Brush.Surface.Recessed") as Brush);
            double radius = TryFindResource("CornerRadius.S") is CornerRadius corner ? corner.TopLeft : 12;

            // Interactive here, unlike the hover preview: this is a page you came to in order to
            // watch something, so the player gets real controls and sound.
            _player.Show(videoId, bounds, videoId, background, radius, interactive: true);
        }
        catch (InvalidOperationException)
        {
            // Not currently connected to a rendered tree; the next layout pass will retry.
        }
    }

    private static string ToCssColor(Brush? brush) =>
        brush is SolidColorBrush solid
            ? $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}"
            : "#00000000";
}
