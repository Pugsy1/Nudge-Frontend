using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.App.ViewModels;

/// <summary>
/// One tile in the library grid. Wraps a scanned <see cref="VpxTableFile"/> with the small amount
/// of extra display logic the grid needs.
/// </summary>
public sealed partial class TableTileViewModel : ObservableObject
{
    private readonly IArtworkProvider _artworkProvider;
    private readonly ILogger _logger;

    private bool _artworkRequested;
    private CancellationTokenSource? _artworkCancellation;

    public TableTileViewModel(VpxTableFile table, IArtworkProvider artworkProvider, ILogger logger)
    {
        Table = table;
        _artworkProvider = artworkProvider;
        _logger = logger;
    }

    public VpxTableFile Table { get; }

    /// <summary>User-supplied title override from the customization page - null keeps Table.DisplayTitle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(Initial))]
    private string? _customTitle;

    /// <summary>User-supplied "made by" credit override, shown in <see cref="Subtitle"/> in place of Table.DisplayManufacturer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private string? _customAuthor;

    /// <summary>User-supplied release-date override (free text), shown in <see cref="Subtitle"/> in place of Table.DisplayYear.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private string? _customDate;

    public string DisplayTitle => string.IsNullOrWhiteSpace(CustomTitle) ? Table.DisplayTitle : CustomTitle;

    /// <summary>Null when the table's year is unknown - used by the year sort to group those last.</summary>
    public int? Year => Table.DisplayYear;

    public string Subtitle
    {
        get
        {
            string? manufacturer = string.IsNullOrWhiteSpace(CustomAuthor) ? Table.DisplayManufacturer : CustomAuthor;
            string? date = string.IsNullOrWhiteSpace(CustomDate) ? Table.DisplayYear?.ToString() : CustomDate;

            if (manufacturer is not null && date is not null)
            {
                return $"{manufacturer} • {date}";
            }

            return manufacturer ?? date ?? string.Empty;
        }
    }

    /// <summary>
    /// Whether this table is starred. An observable property (not a plain field/getter, unlike the
    /// rest of this class) because the grid needs to re-sort live when it changes and the star
    /// button's own fill needs to update immediately on click - see
    /// LibraryViewModel.TablesView.IsLiveSorting.
    /// </summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>
    /// How confident Nudge is that it identified this table correctly. Surfaced as a small status
    /// lamp on the tile, behind a settings toggle - the underlying data has always been computed
    /// (see AGENTS.md section 7, "confidence is a first-class concept"), it just wasn't shown.
    /// </summary>
    public Confidence Confidence => Table.Confidence;

    public string ConfidenceLabel => Table.Confidence switch
    {
        Core.Models.Confidence.High => "Identified confidently",
        Core.Models.Confidence.Medium => "Probably identified correctly",
        Core.Models.Confidence.Low => "Poorly identified - check this table's details",
        _ => "Not identified"
    };

    /// <summary>The reasoning behind the identification, shown as the tile's tooltip when lamps are on.</summary>
    public string EvidenceSummary => Table.Evidence.Summary;

    /// <summary>
    /// Placeholder art shown until (and unless) <see cref="Artwork"/> resolves - just the title's
    /// first letter, so a scanned library reads as a grid of distinct tiles rather than a bare list
    /// even for a table nothing was found for.
    /// </summary>
    public string Initial => string.IsNullOrWhiteSpace(DisplayTitle)
        ? "?"
        : char.ToUpperInvariant(DisplayTitle.Trim()[0]).ToString();

    /// <summary>
    /// Real artwork for this table, once <see cref="BeginLoadArtwork"/> has resolved one - null until
    /// then (and permanently, for a table nothing was found for). Already decoded and sized for
    /// display by <see cref="IArtworkProvider"/>; this just turns the encoded bytes into something
    /// WPF's Image/ImageBrush can bind to directly.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtwork))]
    private ImageSource? _artwork;

    public bool HasArtwork => Artwork is not null;

    /// <summary>
    /// Local video file to play over this tile's artwork on hover, when that setting is on - set from
    /// the table's own customization (NudgeSettings.TableCustomization.VideoPath), null for the many
    /// tables that have none. Bound straight to a MediaElement's Source in the tile template.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVideo))]
    private string? _videoPath;

    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoPath);

    /// <summary>
    /// Whether the automatic video lookup has already run for this tile. Distinguishes "not looked
    /// yet" from "looked, found nothing" - without it a table with no video would re-probe the disk
    /// on every single hover, which is the common case across most of a library.
    /// Not an ObservableProperty: it is bookkeeping for
    /// <see cref="LibraryViewModel.ResolveHoverVideo"/>, never bound to anything.
    /// </summary>
    public bool VideoLookupDone { get; set; }

    /// <summary>A YouTube video id chosen on this table's customization page, played on hover when there is no local video file. Null means fall back to whatever the trailer provider matches automatically.</summary>
    [ObservableProperty]
    private string? _trailerYouTubeId;

    /// <summary>
    /// The tile the controller is currently on. Deliberately a property of the view model rather
    /// than WPF keyboard focus: focus traversal across a virtualizing panel silently fails to move
    /// onto containers that have not been realized yet, which made controller navigation do nothing
    /// at all. An explicit index the view model owns always knows where it is, realized or not.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Hidden from the library entirely, from the customization page's "Hide from Nudge" toggle - set
    /// from NudgeSettings.TableCustomization.IsHidden (LibraryViewModel.ApplyTables) and included in
    /// TablesView.LiveFilteringProperties, so flipping it live drops or restores this tile from every
    /// layout immediately, the same way IsFavorite already does for the "Favourites only" filter.
    /// </summary>
    [ObservableProperty]
    private bool _isHidden;

    private Task _artworkLoadTask = Task.CompletedTask;

    /// <summary>
    /// Starts fetching real artwork for this tile, if it hasn't already been requested. Called from
    /// the tile's container Loaded event (LibraryView.xaml.cs), not eagerly for every table at once -
    /// with up to 1,000 tables in the library (AGENTS.md's performance budget), only ever asking
    /// the provider for tiles that have actually been realized on screen is what keeps scrolling
    /// smooth. Never blocks the caller: the placeholder initial stays up until this resolves, if it
    /// ever does - IArtworkProvider treats "nothing found" as an ordinary result, not an error.
    /// </summary>
    public void BeginLoadArtwork() => _ = EnsureArtworkLoadedAsync();

    /// <summary>
    /// The awaitable form <see cref="BeginLoadArtwork"/> wraps - used directly by
    /// LibraryViewModel's "Find missing artwork" batch action, which needs to know when each tile's
    /// fetch actually finishes rather than firing it and moving on. Safe to call even if a fetch is
    /// already in flight (from the tile having been scrolled into view) or already finished: either
    /// way this returns the same task rather than starting a second redundant request.
    /// </summary>
    public Task EnsureArtworkLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_artworkRequested)
        {
            return _artworkLoadTask;
        }

        _artworkRequested = true;
        _artworkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _artworkLoadTask = LoadArtworkAsync(_artworkCancellation.Token);
        return _artworkLoadTask;
    }

    /// <summary>
    /// Loads a user-chosen local image file directly as this tile's artwork, overriding whatever
    /// IArtworkProvider found (or didn't) - set from the table customization page
    /// (TableCustomizationViewModel.SaveAsync) and re-applied on every future launch straight from
    /// NudgeSettings.TableCustomizations (LibraryViewModel.ApplyTables), bypassing the provider
    /// entirely rather than racing it. Marks artwork as already "requested" so an ordinary
    /// BeginLoadArtwork from scrolling can never overwrite a custom choice with a provider lookup.
    /// </summary>
    public void SetCustomArtwork(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath);
            bitmap.EndInit();
            bitmap.Freeze();

            Artwork = bitmap;
            _artworkRequested = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load custom artwork {Path} for {Table}.", filePath, Table.Path);
        }
    }

    /// <summary>
    /// Applies already-decoded image bytes directly as this tile's artwork - used when the user picks
    /// a candidate from the artwork browser (TableCustomizationViewModel.SelectArtworkCandidateAsync),
    /// where IArtworkBrowser.SelectAsync has already downloaded, resized, and permanently cached the
    /// chosen image and simply hands the bytes back. Marks artwork as already "requested" for the same
    /// reason SetCustomArtwork does - so an ordinary BeginLoadArtwork from scrolling never overwrites
    /// this pick with a fresh provider lookup.
    /// </summary>
    public void SetArtworkFromBytes(byte[] data)
    {
        try
        {
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(data))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            bitmap.Freeze();
            Artwork = bitmap;
            _artworkRequested = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply picked artwork for {Table}.", Table.Path);
        }
    }

    /// <summary>
    /// Cancels an in-flight fetch and allows a later BeginLoadArtwork to try again - called from the
    /// tile's container Unloaded event. A virtualized grid realizes and derealizes far more
    /// containers than actually stay on screen while someone scrolls quickly, so most in-flight
    /// fetches for tiles that scrolled back out of view are wasted network/disk work worth cutting
    /// short rather than letting finish unseen.
    /// </summary>
    public void CancelLoadArtwork()
    {
        _artworkCancellation?.Cancel();
        _artworkCancellation = null;
        _artworkRequested = false;
    }

    private async Task LoadArtworkAsync(CancellationToken cancellationToken)
    {
        try
        {
            Result<ArtworkImage> result = await _artworkProvider.GetArtworkAsync(Table, cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested || result.IsFailure)
            {
                return;
            }

            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(result.Value.Data))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            bitmap.Freeze();
            Artwork = bitmap;
        }
        catch (OperationCanceledException)
        {
            // Expected when the tile scrolled out of view before the fetch finished.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load artwork for {Path}.", Table.Path);
        }
    }
}
