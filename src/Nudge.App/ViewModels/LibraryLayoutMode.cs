namespace Nudge.App.ViewModels;

/// <summary>How the library screen arranges its tables - the library header's "Layout" dropdown.</summary>
public enum LibraryLayoutMode
{
    /// <summary>The default: a virtualized, wrapping grid of tiles (LibraryView.xaml's TableTileTemplate).</summary>
    Grid,

    /// <summary>
    /// A small window of tiles arranged along a shallow arc, the centred one large and in focus,
    /// others receding to either side - a coverflow-style "ring" rather than a flat grid. Not the
    /// default: it shows far fewer tables at once than the grid does, so it suits browsing a library
    /// one table at a time more than scanning a whole collection.
    /// </summary>
    Carousel,

    /// <summary>
    /// A dense, single-column virtualized list - a small thumbnail, title, and subtitle per row.
    /// Trades the grid's browsing-by-artwork feel for information density: far more titles fit on
    /// screen at once, which suits scanning or searching a large library by name rather than by look.
    /// </summary>
    List,

    /// <summary>
    /// The same card as Grid, just smaller (Size.Tile.CompactWidth/Height) and packed tighter - a
    /// middle ground between Grid's big, easy-to-browse artwork and List's dense text rows: still a
    /// wall of artwork, just noticeably more of it on screen at once for a large library.
    /// </summary>
    Compact
}
