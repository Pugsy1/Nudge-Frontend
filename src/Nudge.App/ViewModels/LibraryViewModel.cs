using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nudge.App.Services;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.App.ViewModels;

/// <summary>
/// One selectable entry in a settings dropdown - a display label paired with its stored value.
/// <paramref name="Category"/> is optional and only carried through into <see cref="FullLabel"/> -
/// Sort/Layout options simply leave it blank.
/// </summary>
public sealed record OptionItem<T>(string Label, T Value, string Category = "")
{
    /// <summary>
    /// "Category — Label" when there's a category, otherwise just Label. This is the only place
    /// category information shows up - the dropdown binds ItemsSource directly to a plain list via
    /// this property (not a grouped CollectionView; that fought the ComboBox's own SelectedItem
    /// sync and was reverted), so it has to carry the context on its own.
    /// </summary>
    public string FullLabel => string.IsNullOrEmpty(Category) ? Label : $"{Category} — {Label}";
}

/// <summary>
/// The library screen: a virtualized grid of the tables scanned for the confirmed installation,
/// with search, sorting, a 2D/VR launch mode switch, and a settings flyout.
/// </summary>
/// <remarks>
/// <see cref="ITableRepository"/> and its <c>NudgeDbContext</c> are registered Scoped (EF Core's
/// default), so this resolves them through a fresh <see cref="IServiceScopeFactory"/> scope per
/// operation rather than holding one for the view model's whole lifetime - see AGENTS.md section 5
/// and the note left in Phase 3's <c>Nudge.App.App.xaml.cs</c>.
///
/// <para><b>Not built here, and why.</b> Some requested features need data that does not exist yet
/// and cannot be produced from the UI layer:</para>
/// <list type="bullet">
/// <item><b>Sort by date added / last played</b>, and <b>playtime tracking</b>: nothing records
/// when Nudge first saw a table or when it was last launched. The database row behind a table
/// stores a size and last-write time for incremental scanning only. This needs new columns and a
/// migration in <c>Nudge.Data</c>.</item>
/// <item><b>Artwork / media scraping</b>: needs an artwork provider in <c>Nudge.Core</c>
/// implemented against the network and disk, which <c>Nudge.App</c> must never do itself
/// (AGENTS.md section 5).</item>
/// </list>
/// <para>Favourites turned out not to need any of that: it is a pure UI preference (a starred-item
/// list), so it persists in the settings file - see <see cref="ISettingsService"/> - the same as
/// theme/sort/confidence, not a database column.</para>
/// </remarks>
public sealed partial class LibraryViewModel : ObservableObject, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVpxLibraryScanner _scanner;
    private readonly ITableFolderWatcher _tableFolderWatcher;
    private readonly ILaunchEngine _launchEngine;
    private readonly IThemeService _themeService;
    private readonly IUiStyleService _uiStyleService;
    private readonly IShadowEffectService _shadowEffectService;
    private readonly IArtworkProvider _artworkProvider;
    private readonly IArtworkBrowser _artworkBrowser;
    private readonly IFilePickerService _filePickerService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly ICustomArtworkStore _customArtworkStore;
    private readonly ITableVideoLocator _tableVideoLocator;
    private readonly ITableTrailerProvider _tableTrailerProvider;
    private readonly IControllerReader _controllerReader;

    /// <summary>Exposed so the library view can drive UI navigation from the same pad, without taking its own DI dependency.</summary>
    public IControllerReader ControllerReader => _controllerReader;

    /// <summary>Saved button-to-key overrides, mirrored from settings so a rebind can be applied without a reload.</summary>
    private Dictionary<string, string> _controllerButtonOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISettingsService _settingsService;
    private readonly IRomNameReader _romNameReader;
    private readonly IRomAvailabilityChecker _romAvailabilityChecker;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<LibraryViewModel> _logger;

    private VpxInstallation? _installation;

    /// <summary>The active Tables-folder watch, if any - disposed and replaced whenever <see cref="ActivateAsync"/> runs again for a different installation, and disposed for good in <see cref="Dispose"/>.</summary>
    private IDisposable? _folderWatch;

    /// <summary>Guards the settings-write path while preferences are being loaded into the UI, so restoring a saved value doesn't immediately re-save it.</summary>
    private bool _isLoadingPreferences;

    /// <summary>
    /// Debounces the search filter: re-evaluating <see cref="FilterTable"/> across every table is
    /// O(n) work, and AGENTS.md's performance budget calls for search results under 50ms even at
    /// 1,000 tables - doing that on every single keystroke while someone is still typing piles up
    /// wastefully during fast typing. Restarted on each keystroke, so only the one after typing
    /// actually pauses does the work.
    /// </summary>
    private readonly DispatcherTimer _searchDebounceTimer;

    public LibraryViewModel(
        IServiceScopeFactory scopeFactory,
        IVpxLibraryScanner scanner,
        ITableFolderWatcher tableFolderWatcher,
        ILaunchEngine launchEngine,
        SetupViewModel setup,
        IThemeService themeService,
        IUiStyleService uiStyleService,
        IShadowEffectService shadowEffectService,
        IArtworkProvider artworkProvider,
        IArtworkBrowser artworkBrowser,
        IFilePickerService filePickerService,
        IFolderPickerService folderPickerService,
        ICustomArtworkStore customArtworkStore,
        ITableVideoLocator tableVideoLocator,
        ITableTrailerProvider tableTrailerProvider,
        IControllerReader controllerReader,
        ISettingsService settingsService,
        IRomNameReader romNameReader,
        IRomAvailabilityChecker romAvailabilityChecker,
        IPathRedactor redactor,
        ILogger<LibraryViewModel> logger)
    {
        _scopeFactory = scopeFactory;
        _scanner = scanner;
        _tableFolderWatcher = tableFolderWatcher;
        _launchEngine = launchEngine;
        _themeService = themeService;
        _uiStyleService = uiStyleService;
        _shadowEffectService = shadowEffectService;
        _artworkProvider = artworkProvider;
        _artworkBrowser = artworkBrowser;
        _filePickerService = filePickerService;
        _folderPickerService = folderPickerService;
        _customArtworkStore = customArtworkStore;
        _tableVideoLocator = tableVideoLocator;
        _tableTrailerProvider = tableTrailerProvider;
        _controllerReader = controllerReader;
        _settingsService = settingsService;
        _romNameReader = romNameReader;
        _romAvailabilityChecker = romAvailabilityChecker;
        _redactor = redactor;
        _logger = logger;

        // Reuses the setup flow's own "change folder" logic rather than duplicating it - picking a
        // different folder is still fundamentally a setup concern.
        ChangeFolderCommand = setup.ChangeFolderCommand;

        Tables = [];
        TablesView = (ListCollectionView)CollectionViewSource.GetDefaultView(Tables);
        TablesView.Filter = FilterTable;

        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            TablesView.Refresh();
            UpdateVisibleCount();
        };

        _carouselMotionTicker.Tick += (_, _) => TickCarouselMotion();

        // Lets the grid react live the instant a star is clicked, instead of needing an explicit
        // Refresh() call from the toggle command itself: IsLiveSorting re-orders "Favourites first"
        // immediately, and IsLiveFiltering drops/adds the tile immediately while "Favourites only"
        // is the active sort (see FilterTable) - unfavoriting the last starred table while looking
        // at that view makes it vanish on the same click, not the next unrelated Refresh().
        TablesView.IsLiveSorting = true;
        TablesView.LiveSortingProperties.Add(nameof(TableTileViewModel.IsFavorite));
        TablesView.IsLiveFiltering = true;
        TablesView.LiveFilteringProperties.Add(nameof(TableTileViewModel.IsFavorite));
        TablesView.LiveFilteringProperties.Add(nameof(TableTileViewModel.IsHidden));

        SortOptions =
        [
            new OptionItem<TableSortOrder>("Title  A → Z", TableSortOrder.TitleAscending),
            new OptionItem<TableSortOrder>("Title  Z → A", TableSortOrder.TitleDescending),
            new OptionItem<TableSortOrder>("Year  newest first", TableSortOrder.YearNewest),
            new OptionItem<TableSortOrder>("Year  oldest first", TableSortOrder.YearOldest),
            new OptionItem<TableSortOrder>("Favourites only", TableSortOrder.FavoritesOnly)
        ];
        _selectedSort = SortOptions[0];

        // "Base — Accent", consistently, for every entry - the previous mix of "Graphite  (dark)"
        // and bare "Jade" (padded with a double space to fake alignment in one case, nothing in the
        // other) read as visually uneven in the dropdown.
        //
        // Grouped by Category (Dark, Light, Calm, Duo) rather than interleaving a theme's dark and
        // light variant next to each other - SettingsView.xaml groups the dropdown by this same
        // value (a CollectionViewSource with a PropertyGroupDescription), so both the list's order
        // and its visible section headers agree on the same four buckets.
        ThemeOptions =
        [
            new OptionItem<AppTheme>("Amber", AppTheme.Dark, "Dark"),
            new OptionItem<AppTheme>("Jade", AppTheme.Jade, "Dark"),
            new OptionItem<AppTheme>("Sapphire", AppTheme.Sapphire, "Dark"),
            new OptionItem<AppTheme>("Crimson", AppTheme.Crimson, "Dark"),
            new OptionItem<AppTheme>("Chrome", AppTheme.Chrome, "Dark"),
            new OptionItem<AppTheme>("Hulk", AppTheme.Hulk, "Dark"),
            new OptionItem<AppTheme>("Amethyst", AppTheme.Amethyst, "Dark"),
            new OptionItem<AppTheme>("Rose", AppTheme.Rose, "Dark"),
            new OptionItem<AppTheme>("Teal", AppTheme.Teal, "Dark"),
            new OptionItem<AppTheme>("Coral", AppTheme.Coral, "Dark"),
            new OptionItem<AppTheme>("Indigo", AppTheme.Indigo, "Dark"),
            new OptionItem<AppTheme>("Lime", AppTheme.Lime, "Dark"),
            new OptionItem<AppTheme>("Magenta", AppTheme.Magenta, "Dark"),
            new OptionItem<AppTheme>("Slate", AppTheme.Slate, "Dark"),
            new OptionItem<AppTheme>("Copper", AppTheme.Copper, "Dark"),

            new OptionItem<AppTheme>("Amber", AppTheme.Light, "Light"),
            new OptionItem<AppTheme>("Jade", AppTheme.JadeLight, "Light"),
            new OptionItem<AppTheme>("Sapphire", AppTheme.SapphireLight, "Light"),
            new OptionItem<AppTheme>("Crimson", AppTheme.CrimsonLight, "Light"),
            new OptionItem<AppTheme>("Chrome", AppTheme.ChromeLight, "Light"),
            new OptionItem<AppTheme>("Hulk", AppTheme.HulkLight, "Light"),
            new OptionItem<AppTheme>("Amethyst", AppTheme.AmethystLight, "Light"),
            new OptionItem<AppTheme>("Rose", AppTheme.RoseLight, "Light"),
            new OptionItem<AppTheme>("Teal", AppTheme.TealLight, "Light"),
            new OptionItem<AppTheme>("Coral", AppTheme.CoralLight, "Light"),
            new OptionItem<AppTheme>("Indigo", AppTheme.IndigoLight, "Light"),
            new OptionItem<AppTheme>("Lime", AppTheme.LimeLight, "Light"),
            new OptionItem<AppTheme>("Magenta", AppTheme.MagentaLight, "Light"),
            new OptionItem<AppTheme>("Slate", AppTheme.SlateLight, "Light"),
            new OptionItem<AppTheme>("Copper", AppTheme.CopperLight, "Light"),

            new OptionItem<AppTheme>("Sage", AppTheme.Sage, "Calm"),
            new OptionItem<AppTheme>("Dune", AppTheme.Dune, "Calm"),

            new OptionItem<AppTheme>("Watermelon", AppTheme.Watermelon, "Duo"),
            new OptionItem<AppTheme>("Blue Raspberry", AppTheme.BlueRaspberry, "Duo"),
            new OptionItem<AppTheme>("Citrus", AppTheme.Citrus, "Duo"),
            new OptionItem<AppTheme>("Cosmic", AppTheme.Cosmic, "Duo"),

            // True black - every surface between pure black and white, no hue anywhere except the
            // accent itself, so an OLED panel actually turns pixels off. Grouped on its own rather
            // than folded into "Dark", since none of that category's soft glow/gradient surfaces
            // apply here at all - these are a different material, not just another hue of it. Bare
            // colour names, not "True Black — White" etc. - OptionItem.FullLabel already prepends the
            // Category ("OLED"), so that combination would otherwise read as the redundant
            // "OLED — True Black — White".
            new OptionItem<AppTheme>("White", AppTheme.Oled, "OLED"),
            new OptionItem<AppTheme>("Red", AppTheme.OledRed, "OLED"),
            new OptionItem<AppTheme>("Blue", AppTheme.OledBlue, "OLED"),
            new OptionItem<AppTheme>("Green", AppTheme.OledGreen, "OLED"),
            new OptionItem<AppTheme>("Purple", AppTheme.OledPurple, "OLED")
        ];
        _selectedTheme = ThemeOptions[0];

        UiStyleOptions =
        [
            new OptionItem<AppUiStyle>("Pin  soft and subtle", AppUiStyle.Pin),
            new OptionItem<AppUiStyle>("Relief  deep and sculpted", AppUiStyle.Relief)
        ];
        _selectedUiStyle = UiStyleOptions[0];

        LayoutModeOptions =
        [
            new OptionItem<LibraryLayoutMode>("Grid", LibraryLayoutMode.Grid),
            new OptionItem<LibraryLayoutMode>("Compact", LibraryLayoutMode.Compact),
            new OptionItem<LibraryLayoutMode>("Carousel", LibraryLayoutMode.Carousel),
            new OptionItem<LibraryLayoutMode>("List", LibraryLayoutMode.List)
        ];
        _selectedLayoutMode = LayoutModeOptions[0];

        _selectedArtworkSource = ArtworkSourceOptions[0];

        ApplySort();
    }

    public ObservableCollection<TableTileViewModel> Tables { get; }

    public ListCollectionView TablesView { get; }

    public IRelayCommand ChangeFolderCommand { get; }

    public IReadOnlyList<OptionItem<TableSortOrder>> SortOptions { get; }

    public IReadOnlyList<OptionItem<AppTheme>> ThemeOptions { get; }

    /// <summary>
    /// The interface's material, orthogonal to <see cref="ThemeOptions"/> - style decides shape and
    /// depth, theme decides colour, and every combination of the two is valid.
    /// </summary>
    public IReadOnlyList<OptionItem<AppUiStyle>> UiStyleOptions { get; }

    public IReadOnlyList<OptionItem<LibraryLayoutMode>> LayoutModeOptions { get; }

    /// <summary>
    /// The carousel's current window of tiles - a small slice of <see cref="TablesView"/> (see
    /// <see cref="CarouselWindowSize"/>), not the whole library. Kept deliberately small so switching
    /// to the ring layout never has to realize anywhere near all 1,000 tables at once, the same
    /// virtualization goal <c>VirtualizingWrapPanel</c> serves for the grid (AGENTS.md section 8) -
    /// just achieved here by only ever putting a handful of items in the bound collection at all,
    /// rather than by a custom virtualizing panel.
    /// </summary>
    public ObservableCollection<TableTileViewModel> CarouselWindow { get; } = [];

    [ObservableProperty]
    private string _installationDisplayName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    [NotifyPropertyChangedFor(nameof(HasNoSearchResults))]
    private string _searchText = string.Empty;

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTables))]
    [NotifyPropertyChangedFor(nameof(IsInitialScanning))]
    [NotifyPropertyChangedFor(nameof(IsRescanning))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    [NotifyPropertyChangedFor(nameof(ShowStatusLine))]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    /// <summary>The thin status line under the header - suppressed while the launch overlay is up, since that already shows the same message.</summary>
    public bool ShowStatusLine => HasStatusMessage && !IsLaunching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTables))]
    [NotifyPropertyChangedFor(nameof(HasTables))]
    [NotifyPropertyChangedFor(nameof(IsInitialScanning))]
    [NotifyPropertyChangedFor(nameof(IsRescanning))]
    private int _tableCount;

    /// <summary>How many tiles survive the current search filter - drives the "nothing matched" empty state.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSearchResults))]
    [NotifyPropertyChangedFor(nameof(HasNoFavorites))]
    [NotifyPropertyChangedFor(nameof(HasVisibleTables))]
    [NotifyPropertyChangedFor(nameof(ShowGrid))]
    [NotifyPropertyChangedFor(nameof(ShowCompact))]
    [NotifyPropertyChangedFor(nameof(ShowCarousel))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    private int _visibleTableCount;

    /// <summary>True while the very first scan (nothing loaded from the database yet) is in flight.</summary>
    public bool IsInitialScanning => IsScanning && TableCount == 0;

    /// <summary>
    /// A rescan of a library that already has tables loaded - shown as a slim determinate bar under
    /// the header (see ScanProgressPercent) rather than the big centred indeterminate one
    /// IsInitialScanning uses, since the existing tiles stay on screen the whole time a rescan runs.
    /// </summary>
    public bool IsRescanning => IsScanning && TableCount > 0;

    /// <summary>
    /// How far the current scan has gotten, 0-100 - fed by the same IProgress&lt;ScanProgress&gt; the
    /// scanner already reported per-file counts through (ScanAsync's own remarks), just not
    /// previously wired to anything. Reset to 0 at the start of every scan so a fast rescan doesn't
    /// briefly flash whatever percentage the last one finished at.
    /// </summary>
    [ObservableProperty]
    private double _scanProgressPercent;

    /// <summary>True once a scan has actually run and the installation genuinely holds no tables.</summary>
    public bool HasNoTables => !IsScanning && TableCount == 0;

    public bool HasTables => TableCount > 0;

    /// <summary>The library has tables, but the current search matched none of them.</summary>
    public bool HasNoSearchResults => TableCount > 0 && VisibleTableCount == 0 && HasSearchText
                                       && SelectedSort?.Value != TableSortOrder.FavoritesOnly;

    /// <summary>"Favourites only" is selected and nothing is starred yet - distinct from a search matching nothing.</summary>
    public bool HasNoFavorites => TableCount > 0 && VisibleTableCount == 0 && !HasSearchText
                                   && SelectedSort?.Value == TableSortOrder.FavoritesOnly;

    public bool HasVisibleTables => VisibleTableCount > 0;

    /// <summary>Grid layout, with tables to show. The "no tables"/"no results" empty states are shown independently of layout mode, so every layout shares them rather than each needing its own.</summary>
    public bool ShowGrid => HasVisibleTables && SelectedLayoutMode?.Value == LibraryLayoutMode.Grid;

    /// <summary>Compact layout, with tables to show - the same card as Grid, just smaller and packed tighter (Size.Tile.CompactWidth/Height).</summary>
    public bool ShowCompact => HasVisibleTables && SelectedLayoutMode?.Value == LibraryLayoutMode.Compact;

    /// <summary>Ring layout, with tables to show.</summary>
    public bool ShowCarousel => HasVisibleTables && IsCarouselLayout;

    /// <summary>List layout, with tables to show.</summary>
    public bool ShowList => HasVisibleTables && SelectedLayoutMode?.Value == LibraryLayoutMode.List;

    // ---------------------------------------------------------------- Preferences

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSearchResults))]
    [NotifyPropertyChangedFor(nameof(HasNoFavorites))]
    private OptionItem<TableSortOrder> _selectedSort;

    [ObservableProperty]
    private OptionItem<AppTheme> _selectedTheme;

    [ObservableProperty]
    private OptionItem<AppUiStyle> _selectedUiStyle;

    [ObservableProperty]
    private OptionItem<LibraryLayoutMode> _selectedLayoutMode;

    public bool IsCarouselLayout => SelectedLayoutMode?.Value == LibraryLayoutMode.Carousel;

    public bool IsGridLayout => SelectedLayoutMode?.Value == LibraryLayoutMode.Grid;

    /// <summary>How many slots either side of the centred tile the ring shows - 3 either side, 7 total, when the library has enough tables to fill it. Fewer, more widely-spaced tiles (CarouselSlotConverter's AngleStepDegrees) is what reads as an actual round ring rather than a crowd of overlapping cards.</summary>
    private const int CarouselWindowRadius = 3;

    private const int CarouselWindowSize = (CarouselWindowRadius * 2) + 1;

    /// <summary>
    /// The ring's continuous scroll position - a fractional, deliberately <b>unbounded</b> logical
    /// coordinate, not clamped to <c>[0, count)</c>. Mouse wheel deltas (LibraryView.xaml.cs's
    /// OnCarouselMouseWheel) feed into <see cref="_carouselVelocity"/> rather than changing this
    /// directly (see CarouselScroll's remarks on why - momentum, not an instant jump per notch), which
    /// <see cref="TickCarouselMotion"/> then spends against this value every frame. No ceiling or
    /// floor either way: scrolling straight past either end just keeps this number growing/shrinking
    /// rather than hitting a wall, which is what makes the ring loop.
    /// <see cref="WrapIndex"/> is the only place this ever gets folded back into a real table index;
    /// the position itself, and <see cref="CarouselWindowStart"/> alongside it, stay on one
    /// continuous unwrapped number line so CarouselSlotConverter's arc math never has to special-case
    /// a wrap - only which physical table sits at a given logical slot changes, never the geometry.
    /// <see cref="TickCarouselMotion"/> eases this to the nearest whole index once momentum decays,
    /// the same way a real wheel settles into a detent.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CarouselPositionLabel))]
    private double _carouselPosition;

    /// <summary>The first logical slot in <see cref="CarouselWindow"/>, in the same unbounded coordinate space as <see cref="CarouselPosition"/> - added to a container's own AlternationIndex (CarouselSlotConverter) to recover that slot's position relative to the current scroll position.</summary>
    [ObservableProperty]
    private int _carouselWindowStart = -1;

    public string CarouselPositionLabel => VisibleTableCount == 0
        ? string.Empty
        : $"{WrapIndex((int)Math.Round(CarouselPosition), VisibleTableCount) + 1} of {VisibleTableCount}";

    /// <summary>
    /// Wraps an unbounded logical index (see <see cref="CarouselPosition"/>'s remarks) into the
    /// valid <c>[0, count)</c> range a real lookup into <see cref="TablesView"/> needs - plain C# `%`
    /// returns a negative result for a negative left-hand side, which this corrects for.
    /// </summary>
    private static int WrapIndex(int index, int count) => ((index % count) + count) % count;

    /// <summary>
    /// Drives the ring's momentum: every wheel notch adds to this rather than moving
    /// <see cref="CarouselPosition"/> directly, and <see cref="_carouselMotionTicker"/> applies it
    /// every frame with friction, so a flick keeps gliding and gradually decelerating instead of the
    /// position jumping straight to wherever the last discrete wheel event landed. This is the fix
    /// for the ring reading as "cramped"/"cheap" rather than a genuinely spacious scroll - a real
    /// wheel or touch scroll always carries momentum; snapping position 1:1 to input never did.
    /// </summary>
    private double _carouselVelocity;

    /// <summary>
    /// Set only by <see cref="CarouselTileClick"/> (bringing a clicked side tile to centre) - while
    /// this has a value, <see cref="TickCarouselMotion"/> eases straight toward it instead of coasting
    /// on velocity or homing to the nearest whole table. Cleared once reached, or by the next scroll.
    /// </summary>
    private double? _carouselExplicitTarget;

    /// <summary>The single timer behind all ring motion - coasting, homing to the nearest table once velocity decays, and easing to an explicit click target.</summary>
    private readonly DispatcherTimer _carouselMotionTicker = new() { Interval = TimeSpan.FromMilliseconds(16) };

    // Tuned so one ordinary wheel notch lands close to 1 table travelled in total.
    private const double CarouselWheelGain = 0.15;

    // While coasting on wheel-supplied velocity (no explicit target locked in yet), friction alone
    // slows it down - CarouselSpringStiffness only ever pulls toward a *fixed* target, see below for
    // why continuously re-aiming at "whatever's nearest right now" was the actual bug behind
    // "doesn't scroll with ease": mid-coast, the nearest whole table keeps changing out from under
    // the spring as the position sweeps past each one, so the pull constantly reverses direction
    // instead of ever cleanly finishing a move - it fights the very momentum that's supposed to
    // carry it to the next table.
    private const double CarouselFriction = 0.90;

    // Once coasting velocity decays under this, TickCarouselMotion locks in the nearest whole table
    // as a genuine fixed target (the same _carouselExplicitTarget a clicked side-tile uses) instead
    // of continuing to re-evaluate "nearest" every frame. Small enough that the lock-in itself is
    // never a visible jump: at that speed the position is already close to settled.
    private const double CarouselSettleVelocityThreshold = 0.02;

    // How hard the spring pulls once a target is actually fixed (a locked-in settle point, or a
    // clicked side tile) - this phase never re-aims, so there's no risk of the fight described above;
    // it only has to feel snappy arriving at a target that never moves.
    private const double CarouselSpringStiffness = 0.18;
    private const double CarouselSpringDamping = 0.72;

    /// <summary>Shows the per-tile identification-confidence lamp. Off by default - the data has always been computed, but it is diagnostic detail most of the time.</summary>
    [ObservableProperty]
    private bool _showConfidence;

    /// <summary>
    /// Opt-in, off by default: lets IArtworkProvider fetch artwork it can't find locally from the
    /// network. This flag is only ever read and persisted here - whether a given GetArtworkAsync call
    /// actually honours it is IArtworkProvider's own concern (it reads the same NudgeSettings itself),
    /// per AGENTS.md's rule that Nudge.App consumes Core interfaces rather than reimplementing what
    /// they do.
    /// </summary>
    [ObservableProperty]
    private bool _fetchArtworkFromInternet;

    /// <summary>
    /// Which artwork source to try first - see NudgeSettings.DefaultArtworkSourceName. The composite
    /// provider (IArtworkProvider's only real registration - see Nudge.Media's own DI setup) still
    /// falls through to every other configured source afterward if this one finds nothing; this only
    /// decides the order, never an exclusive choice. Selectable regardless of whether Google Images
    /// is actually configured below - an unconfigured source is simply skipped by the provider
    /// itself, the same ordinary "no match" outcome as a table vps-db doesn't have either.
    /// </summary>
    public IReadOnlyList<OptionItem<string>> ArtworkSourceOptions { get; } =
    [
        new OptionItem<string>("vps-db", "vps-db"),
        new OptionItem<string>("Google Images", "Google Images")
    ];

    [ObservableProperty]
    private OptionItem<string> _selectedArtworkSource;

    /// <summary>
    /// The user's own Google Cloud API key for the optional Google Custom Search artwork source - see
    /// NudgeSettings.GoogleCustomSearchApiKey for why this has to be a key the user obtains
    /// themselves (Google's Terms of Service forbid scraping Search directly; this is the sanctioned
    /// API instead). Null/empty leaves that source unconfigured, and the composite provider silently
    /// skips it - not an error, the same as vps-db having no match for a table.
    /// </summary>
    [ObservableProperty]
    private string _googleCustomSearchApiKey = string.Empty;

    /// <summary>The Programmable Search Engine ID ("cx") paired with GoogleCustomSearchApiKey - both are required together.</summary>
    [ObservableProperty]
    private string _googleCustomSearchEngineId = string.Empty;

    /// <summary>Tints each tile's title band with the active theme's accent at low opacity - see NudgeSettings.TintTableBanner.</summary>
    [ObservableProperty]
    private bool _tintTableBanner;

    /// <summary>
    /// Every Xbox button and the key it sends to Visual Pinball. Always active - controller input
    /// sits alongside keyboard and mouse rather than being a mode to switch on - so this list is
    /// about setup, not about enabling anything.
    /// </summary>
    public ObservableCollection<ControllerBindingViewModel> ControllerBindings { get; } = [];

    /// <summary>True while any row is waiting for a key press, so the view can show one shared hint.</summary>
    public bool IsRebinding => ControllerBindings.Any(b => b.IsListening);

    /// <summary>Set when a captured key isn't one Nudge can replay, rather than failing silently.</summary>
    [ObservableProperty]
    private string _controllerHint = string.Empty;

    /// <summary>Whether a pad is plugged in, so the section can say so instead of looking inert.</summary>
    [ObservableProperty]
    private bool _isControllerConnected;

    /// <summary>Plays a table's own video over its artwork while hovered - see NudgeSettings.EnableMediaTrailers.</summary>
    [ObservableProperty]
    private bool _enableMediaTrailers;

    /// <summary>Whether those hover videos play silently. On by default.</summary>
    [ObservableProperty]
    private bool _muteMediaTrailers = true;

    /// <summary>
    /// When on, launching uses the installation's VR-capable build instead of the desktop one. Only
    /// meaningful when <see cref="HasVr"/> is true.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDesktopMode))]
    private bool _isVrMode;

    /// <summary>The inverse of <see cref="IsVrMode"/>, so the 2D segment can bind two-way without a converter.</summary>
    public bool IsDesktopMode
    {
        get => !IsVrMode;
        set
        {
            if (value)
            {
                IsVrMode = false;
            }
        }
    }

    /// <summary>True when the confirmed installation has a recognised VR-capable build - enables the 2D/VR switch.</summary>
    [ObservableProperty]
    private bool _hasVr;

    /// <summary>How many tiles fit per row, 3-8. Bound to VirtualizingWrapPanel.Columns - see the note there for how a fixed column count reshapes tile size instead of the usual "as many ItemWidth-sized columns as fit".</summary>
    [ObservableProperty]
    private int _tablesPerRow = 8;

    [ObservableProperty]
    private bool _isSettingsOpen;

    /// <summary>
    /// Non-null while the per-table customization page is open for one specific table - ShellViewModel
    /// watches this the same way it watches IsSettingsOpen, swapping to this view model instead of
    /// Library/Settings whenever it's set, and back once TableCustomizationViewModel.Back clears it.
    /// A fresh instance per table, not a long-lived one like SettingsViewModel, since its content is
    /// entirely about whichever table it was opened for.
    /// </summary>
    [ObservableProperty]
    private TableCustomizationViewModel? _editingTableViewModel;

    [RelayCommand]
    private void OpenTableCustomization(TableTileViewModel? tile)
    {
        if (tile is null)
        {
            return;
        }

        EditingTableViewModel = new TableCustomizationViewModel(
            tile,
            this,
            _settingsService,
            _filePickerService,
            _customArtworkStore,
            _tableTrailerProvider,
            _artworkBrowser,
            _romNameReader,
            _romAvailabilityChecker);
    }

    /// <summary>
    /// The read-only details page for one table, when open. Mirrors
    /// <see cref="EditingTableViewModel"/>: a fresh instance per table, and ShellViewModel switches
    /// the whole window to it.
    /// </summary>
    [ObservableProperty]
    private TableDetailsViewModel? _detailsTableViewModel;

    [RelayCommand]
    private void OpenTableDetails(TableTileViewModel? tile)
    {
        if (tile is null)
        {
            return;
        }

        DetailsTableViewModel = new TableDetailsViewModel(tile, this, _tableTrailerProvider);
    }

    public void CloseTableDetails() => DetailsTableViewModel = null;

    // ---------------------------------------------------------------- Controller navigation

    /// <summary>
    /// True once the pad has been used, false again as soon as the mouse moves. Drives the selection
    /// ring: a highlighted tile is exactly what you need when navigating with a stick and pure
    /// clutter when you are pointing at things directly, so the UI follows whichever input is
    /// actually in use rather than making it a setting.
    /// </summary>
    [ObservableProperty]
    private bool _isControllerMode;

    /// <summary>The tile the controller is currently on.</summary>
    [ObservableProperty]
    private TableTileViewModel? _selectedTile;

    /// <summary>
    /// The button legend shown in the header while a pad is in use. Only appears in controller mode
    /// - with a mouse it is telling you about buttons you are not holding.
    ///
    /// Deliberately FIXED, not swapped as the focus moves. An earlier version changed the legend for
    /// the header, the slider and the keyboard, which was accurate but meant the one strip of the
    /// screen you glance at kept rearranging itself underneath you - the flicker cost more than the
    /// accuracy bought. Contexts that genuinely rebind the buttons (the slider, the keyboard) state
    /// their own actions inside the thing you are looking at instead.
    /// </summary>
    public IReadOnlyList<ControllerHint> ControllerHints { get; } =
    [
        new("A", "Play"),
        new("X", "Details"),
        new("Y", "Customize"),
        new("LB", "Favourite"),
        new("Start", "Settings")
    ];

    /// <summary>
    /// The keyboard's own legend, shown inside the keyboard card. These buttons genuinely mean
    /// something else while typing, so they are stated where the eye already is rather than by
    /// rewriting <see cref="ControllerHints"/> out from under the user.
    /// </summary>
    public IReadOnlyList<ControllerHint> OnScreenKeyboardHints { get; } =
    [
        new("A", "Type"),
        new("X", "Backspace"),
        new("LB", "Clear"),
        new("B", "Done")
    ];

    /// <summary>
    /// True while the pad is driving the header rather than the grid. Hides the tile selection ring,
    /// so there is only ever one thing on screen claiming to be "where you are".
    /// </summary>
    [ObservableProperty]
    private bool _isHeaderFocused;

    // ---------------------------------------------------------------- On-screen keyboard

    /// <summary>The pad-driven keyboard for the search box. Null until it is first opened.</summary>
    [ObservableProperty]
    private OnScreenKeyboardViewModel? _onScreenKeyboard;

    [ObservableProperty]
    private bool _isOnScreenKeyboardOpen;

    /// <summary>
    /// Opens the keyboard, seeded with whatever is already in the search box so it edits rather than
    /// replaces. Its text is pushed straight back into SearchText on every key, so the library
    /// filters live underneath - the same feedback typing on a real keyboard gives.
    /// </summary>
    public void OpenOnScreenKeyboard()
    {
        if (OnScreenKeyboard is null)
        {
            OnScreenKeyboard = new OnScreenKeyboardViewModel();
            OnScreenKeyboard.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(OnScreenKeyboardViewModel.Text))
                {
                    SearchText = OnScreenKeyboard!.Text;
                }
            };

            // The search key does not perform a search - the results have been filtering live with
            // every keystroke already. It closes the keyboard, which is what "done" means here.
            OnScreenKeyboard.Submitted += CloseOnScreenKeyboard;
        }

        OnScreenKeyboard.Text = SearchText ?? string.Empty;
        OnScreenKeyboard.ResetSelection();
        IsOnScreenKeyboardOpen = true;
    }

    public void CloseOnScreenKeyboard()
    {
        IsOnScreenKeyboardOpen = false;

        // Everything typed while the keyboard was up is applied here, in one pass - see
        // OnSearchTextChanged for why it is deferred rather than run per keystroke.
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    partial void OnSelectedTileChanged(TableTileViewModel? oldValue, TableTileViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    /// <summary>Raised when the selection moves, so the view can scroll that tile into sight.</summary>
    public event Action<TableTileViewModel>? SelectionMoved;

    /// <summary>Called on any controller input - switches the UI into controller mode and makes sure something is selected.</summary>
    public void EnterControllerMode()
    {
        IsControllerMode = true;

        if (SelectedTile is null || !VisibleTables().Contains(SelectedTile))
        {
            SelectFirstVisible();
        }
    }

    /// <summary>Called when the mouse moves - hands the UI back to pointer input.</summary>
    public void ExitControllerMode() => IsControllerMode = false;

    private List<TableTileViewModel> VisibleTables() => TablesView.Cast<TableTileViewModel>().ToList();

    /// <summary>
    /// Whether the selection is on the first row, so pressing up should leave the grid and reach the
    /// header rather than doing nothing. Being unable to get to the search box and the 2D/VR switch
    /// without a mouse is what makes a pad feel like a partial input rather than a real one.
    /// </summary>
    public bool IsSelectionOnTopRow
    {
        get
        {
            List<TableTileViewModel> items = VisibleTables();
            int index = SelectedTile is null ? -1 : items.IndexOf(SelectedTile);
            if (index < 0)
            {
                return true;
            }

            int perRow = SelectedLayoutMode?.Value is LibraryLayoutMode.Grid or LibraryLayoutMode.Compact
                ? Math.Max(1, TablesPerRow)
                : 1;

            return index < perRow;
        }
    }

    private void SelectFirstVisible()
    {
        List<TableTileViewModel> items = VisibleTables();
        if (items.Count > 0)
        {
            SelectedTile = items[0];
            SelectionMoved?.Invoke(items[0]);
        }
    }

    /// <summary>
    /// Moves the selection by whole columns and rows. <paramref name="rows"/> steps by the grid's
    /// current column count, so up/down land on the tile directly above or below rather than the
    /// neighbouring one.
    /// </summary>
    public void MoveSelection(int columns, int rows)
    {
        List<TableTileViewModel> items = VisibleTables();
        if (items.Count == 0)
        {
            return;
        }

        int index = SelectedTile is null ? -1 : items.IndexOf(SelectedTile);
        if (index < 0)
        {
            SelectFirstVisible();
            return;
        }

        // The list and carousel layouts are a single column, so a horizontal step there should move
        // one item rather than doing nothing.
        int perRow = SelectedLayoutMode?.Value is LibraryLayoutMode.Grid or LibraryLayoutMode.Compact
            ? Math.Max(1, TablesPerRow)
            : 1;

        int next = index + columns + (rows * perRow);

        // Clamped rather than wrapped: running off the end of the grid and reappearing at the start
        // is disorienting when you cannot see where the selection went.
        next = Math.Clamp(next, 0, items.Count - 1);
        if (next == index)
        {
            return;
        }

        SelectedTile = items[next];
        SelectionMoved?.Invoke(items[next]);
    }

    // ---------------------------------------------------------------- Random pick

    /// <summary>The table the random picker is currently offering.</summary>
    [ObservableProperty]
    private TableTileViewModel? _randomTable;

    [ObservableProperty]
    private bool _isRandomPickerOpen;

    /// <summary>
    /// Offers a random table, and re-rolls when pressed again from inside the picker.
    ///
    /// Picks from the *visible* tables rather than every scanned one, so hidden tables and whatever
    /// the current search and sort are showing are all respected - being offered a table you have
    /// deliberately hidden, or one filtered out of what you are looking at, would read as a bug.
    /// </summary>
    [RelayCommand]
    private void PickRandomTable()
    {
        List<TableTileViewModel> candidates = TablesView.Cast<TableTileViewModel>().ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        // Avoids offering the same table twice in a row, which for a small library otherwise happens
        // often enough to look like the button did nothing.
        if (candidates.Count > 1 && RandomTable is not null)
        {
            candidates.RemoveAll(t => ReferenceEquals(t, RandomTable));
        }

        RandomTable = candidates[Random.Shared.Next(candidates.Count)];
        IsRandomPickerOpen = true;
    }

    [RelayCommand]
    private void CloseRandomPicker() => IsRandomPickerOpen = false;

    [RelayCommand]
    private void PlayRandomTable()
    {
        TableTileViewModel? tile = RandomTable;
        IsRandomPickerOpen = false;

        if (tile is not null)
        {
            LaunchTableCommand.Execute(tile);
        }
    }

    [RelayCommand]
    private void OpenRandomTableDetails()
    {
        TableTileViewModel? tile = RandomTable;
        IsRandomPickerOpen = false;

        if (tile is not null)
        {
            OpenTableDetailsCommand.Execute(tile);
        }
    }

    /// <summary>
    /// The user's own description for a table, if they wrote one - read from this view model's
    /// settings snapshot so the details page doesn't need its own settings dependency just to show
    /// two strings.
    /// </summary>
    public string? GetCustomDescription(string tablePath) =>
        _tableCustomizations.TryGetValue(tablePath, out TableCustomization? c) ? c.Description : null;

    public string? GetCustomHowToPlay(string tablePath) =>
        _tableCustomizations.TryGetValue(tablePath, out TableCustomization? c) ? c.HowToPlay : null;

    /// <summary>Whether the theme draws its soft drop shadows at all. On by default.</summary>
    [ObservableProperty]
    private bool _enableShadows = true;

    /// <summary>How strong those shadows are, as a percentage of the theme's authored default (25-175, 100 = default). Only meaningful while <see cref="EnableShadows"/> is on.</summary>
    [ObservableProperty]
    private int _shadowIntensityPercent = 100;

    public string ThemeToggleLabel => _themeService.Current == AppTheme.Dark
        ? "Switch to light theme"
        : "Switch to dark theme";

    partial void OnSearchTextChanged(string value)
    {
        // The on-screen keyboard holds the filter pass back entirely until it closes. Refreshing the
        // view re-runs the filter over the whole library and forces the virtualizing panel to
        // re-measure and re-realize tiles, all on the UI thread - perhaps 120ms of work. On a
        // physical keyboard that hides inside the gaps between keystrokes; on a pad, where each
        // letter is a deliberate press, it landed squarely on top of every single one, which is the
        // hitch after each letter and after each backspace. Nothing is lost by waiting: the library
        // is behind a scrim while the keyboard is up, and Search applies it the moment you are done.
        if (IsOnScreenKeyboardOpen)
        {
            return;
        }

        // Restarting rather than letting it run means a keystroke arriving mid-interval pushes the
        // actual filter pass back out, so it only ever fires once typing has actually paused.
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    partial void OnSelectedSortChanged(OptionItem<TableSortOrder> value)
    {
        ApplySort();
        _ = SavePreferencesAsync();
    }

    partial void OnSelectedThemeChanged(OptionItem<AppTheme> value)
    {
        if (value is null)
        {
            return;
        }

        _themeService.Apply(value.Value);
        OnPropertyChanged(nameof(ThemeToggleLabel));

        // Shadow effects hold a plain Color, not a live {DynamicResource} binding (see the remarks
        // on ShadowEffectService), so a theme switch needs this to re-read the new palette's
        // Color.Shadow/Color.Highlight and rebuild every shadow with it - otherwise every shadow
        // would silently keep the previous theme's tint.
        _shadowEffectService.Apply(EnableShadows, ShadowIntensityPercent);

        _ = SavePreferencesAsync();
    }

    partial void OnSelectedUiStyleChanged(OptionItem<AppUiStyle> value)
    {
        if (value is null)
        {
            return;
        }

        _uiStyleService.Apply(value.Value);

        // Same reason the theme switch does this: shadows are built from a plain Color rather than a
        // live DynamicResource binding, and the style overlay redefines the shadow effects outright
        // (Crisp removes most of them), so they have to be rebuilt against whatever the new style
        // actually left in place.
        _shadowEffectService.Apply(EnableShadows, ShadowIntensityPercent);

        _ = SavePreferencesAsync();
    }

    partial void OnShowConfidenceChanged(bool value) => _ = SavePreferencesAsync();

    partial void OnFetchArtworkFromInternetChanged(bool value)
    {
        FindMissingArtworkCommand.NotifyCanExecuteChanged();
        _ = SavePreferencesAsync();
    }

    partial void OnSelectedArtworkSourceChanged(OptionItem<string> value) => _ = SavePreferencesAsync();

    partial void OnGoogleCustomSearchApiKeyChanged(string value) => _ = SavePreferencesAsync();

    partial void OnGoogleCustomSearchEngineIdChanged(string value) => _ = SavePreferencesAsync();

    partial void OnTintTableBannerChanged(bool value) => _ = SavePreferencesAsync();


    partial void OnEnableMediaTrailersChanged(bool value) => _ = SavePreferencesAsync();

    partial void OnMuteMediaTrailersChanged(bool value) => _ = SavePreferencesAsync();

    partial void OnIsVrModeChanged(bool value) => _ = SavePreferencesAsync();

    partial void OnTablesPerRowChanged(int value) => _ = SavePreferencesAsync();

    partial void OnEnableShadowsChanged(bool value)
    {
        _shadowEffectService.Apply(value, ShadowIntensityPercent);
        _ = SavePreferencesAsync();
    }

    partial void OnShadowIntensityPercentChanged(int value)
    {
        _shadowEffectService.Apply(EnableShadows, value);
        _ = SavePreferencesAsync();
    }

    partial void OnSelectedLayoutModeChanged(OptionItem<LibraryLayoutMode> value)
    {
        OnPropertyChanged(nameof(IsCarouselLayout));
        OnPropertyChanged(nameof(IsGridLayout));
        OnPropertyChanged(nameof(ShowGrid));
        OnPropertyChanged(nameof(ShowCompact));
        OnPropertyChanged(nameof(ShowCarousel));
        OnPropertyChanged(nameof(ShowList));
        if (IsCarouselLayout)
        {
            RebuildCarouselWindowIfNeeded();
        }

        _ = SavePreferencesAsync();
    }

    /// <summary>
    /// Applies one mouse wheel gesture's worth of movement as momentum, not a direct position change
    /// - <paramref name="delta"/> (in fractional tables; see CarouselPosition's remarks on why that's
    /// a double) adds to <see cref="_carouselVelocity"/>, which <see cref="TickCarouselMotion"/> then
    /// spends over the following frames. A fast flick therefore keeps gliding after the wheel itself
    /// has already stopped, the same way a real scroll wheel or trackpad feels - applying delta
    /// straight to CarouselPosition (an earlier version of this) made every notch an instant,
    /// un-decaying jump, which read as "cramped"/"cheap" rather than a spacious scroll.
    /// </summary>
    [RelayCommand]
    private void CarouselScroll(double delta)
    {
        if (VisibleTableCount == 0)
        {
            return;
        }

        // A fresh scroll input overrides any pending "bring this clicked tile to centre" - the user
        // scrolling again means that target is no longer what they want.
        _carouselExplicitTarget = null;
        _carouselVelocity += delta * CarouselWheelGain;

        if (!_carouselMotionTicker.IsEnabled)
        {
            _carouselMotionTicker.Start();
        }
    }

    /// <summary>
    /// One frame of ring motion, in one of two phases that share the same velocity the whole time (so
    /// the handoff between them is never a visible jump - see CarouselFriction's remarks for why a
    /// spring that continuously re-aims at "whatever's nearest right now" was the actual bug, not a
    /// fix, for how this used to feel):
    ///
    /// <para><b>Coasting</b> - no target locked in yet. Friction alone slows the wheel-supplied
    /// velocity down; nothing pulls the position anywhere, so a fast flick carries cleanly across
    /// several tables instead of getting fought the whole way.</para>
    ///
    /// <para><b>Settling</b> - once coasting velocity decays far enough, the nearest whole table is
    /// locked in exactly once as <see cref="_carouselExplicitTarget"/> (the same field a clicked side
    /// tile uses), and a stiffer critically-damped spring takes over to arrive at that fixed point
    /// cleanly. Because it only ever pulls toward a target that no longer moves, this phase can be
    /// snappy without the earlier version's tug-of-war.</para>
    /// </summary>
    private void TickCarouselMotion()
    {
        if (_carouselExplicitTarget is not { } target)
        {
            _carouselVelocity *= CarouselFriction;
            CarouselPosition += _carouselVelocity;

            if (Math.Abs(_carouselVelocity) < CarouselSettleVelocityThreshold)
            {
                _carouselExplicitTarget = Math.Round(CarouselPosition);
            }

            RebuildCarouselWindowIfNeeded();
            return;
        }

        double toTarget = target - CarouselPosition;

        _carouselVelocity += toTarget * CarouselSpringStiffness;
        _carouselVelocity *= CarouselSpringDamping;
        CarouselPosition += _carouselVelocity;

        if (Math.Abs(toTarget) < 0.001 && Math.Abs(_carouselVelocity) < 0.001)
        {
            CarouselPosition = target;
            _carouselVelocity = 0;
            _carouselExplicitTarget = null;
            _carouselMotionTicker.Stop();
        }

        RebuildCarouselWindowIfNeeded();
    }

    /// <summary>
    /// Refills <see cref="CarouselWindow"/> only when the window's start index actually needs to
    /// move - not on every fractional change to <see cref="CarouselPosition"/>, which would otherwise
    /// mean a full Clear+Add on every single settle-animation tick (roughly 60 times a second) for no
    /// visible benefit, since most of those ticks land within the same window of tiles.
    ///
    /// <see cref="CarouselWindowStart"/> lives in the same unbounded logical coordinate space as
    /// <see cref="CarouselPosition"/> - it is not itself wrapped into <c>[0, count)</c>, only the
    /// individual real table index each slot resolves to is (via <see cref="WrapIndex"/>). That is
    /// what makes the ring loop: CarouselSlotConverter's offset math (windowStart+alternationIndex
    /// minus position) stays perfectly continuous across the wrap, since neither side of that
    /// subtraction ever jumps: only which physical table happens to sit at a given logical slot does.
    /// A library smaller than the window simply repeats tables around the ring more than once, which
    /// is the correct circular behaviour, not an edge case to special-case away.
    /// </summary>
    private void RebuildCarouselWindowIfNeeded()
    {
        int count = TablesView.Count;
        if (count == 0)
        {
            if (CarouselWindow.Count > 0)
            {
                CarouselWindow.Clear();
            }

            CarouselWindowStart = 0;
            return;
        }

        int center = (int)Math.Round(CarouselPosition);
        int desiredStart = center - CarouselWindowRadius;

        if (desiredStart == CarouselWindowStart && CarouselWindow.Count == CarouselWindowSize)
        {
            return;
        }

        CarouselWindow.Clear();
        for (int i = desiredStart; i < desiredStart + CarouselWindowSize; i++)
        {
            CarouselWindow.Add((TableTileViewModel)TablesView.GetItemAt(WrapIndex(i, count)));
        }

        CarouselWindowStart = desiredStart;
    }

    /// <summary>
    /// The ring's one click handler for every tile, centred or not - clicking the already-centred
    /// tile launches it, the same as a grid tile does; clicking any other visible tile eases the ring
    /// to bring it to the centre instead of launching, so a slightly mis-aimed click on a receding
    /// side tile can never accidentally start Visual Pinball.
    /// </summary>
    [RelayCommand]
    private void CarouselTileClick(TableTileViewModel? tile)
    {
        if (tile is null)
        {
            return;
        }

        int windowRelative = CarouselWindow.IndexOf(tile);
        if (windowRelative < 0)
        {
            return;
        }

        int absoluteIndex = CarouselWindowStart + windowRelative;
        if (absoluteIndex == (int)Math.Round(CarouselPosition))
        {
            if (LaunchTableCommand.CanExecute(tile))
            {
                LaunchTableCommand.Execute(tile);
            }

            return;
        }

        _carouselVelocity = 0;
        _carouselExplicitTarget = absoluteIndex;
        if (!_carouselMotionTicker.IsEnabled)
        {
            _carouselMotionTicker.Start();
        }
    }

    private void ApplySort()
    {
        TablesView.CustomSort = SelectedSort?.Value switch
        {
            TableSortOrder.TitleDescending => new TableTitleComparer(ascending: false),
            TableSortOrder.YearNewest => new TableYearComparer(newestFirst: true),
            TableSortOrder.YearOldest => new TableYearComparer(newestFirst: false),
            TableSortOrder.FavoritesOnly => new TableFavoriteComparer(),
            _ => new TableTitleComparer(ascending: true)
        };

        // FilterTable's result depends on SelectedSort now (FavoritesOnly hides everything else),
        // not just SearchText - CustomSort alone doesn't re-run the filter, so this needs an
        // explicit Refresh() whenever the sort changes, unlike a plain re-order would.
        TablesView.Refresh();
        UpdateVisibleCount();
    }

    private void UpdateVisibleCount()
    {
        VisibleTableCount = TablesView.Count;

        // The ring only ever holds a small window of the filtered list (CarouselWindow's own doc
        // comment), so any change to that list - a search, a sort, a favourite toggling in or out of
        // view - has to re-clamp the position and refill the window, the same as the grid's own
        // ItemsControl reacts to TablesView changing on its own.
        // No clamping - CarouselPosition is an unbounded logical coordinate (its own remarks), so a
        // shrinking list doesn't need to pull it back in line, only WrapIndex needs the new count.
        RebuildCarouselWindowIfNeeded();
    }

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private void SetLaunchMode(string mode) => IsVrMode = mode == "Vr";

    /// <summary>
    /// Flips 2D/VR. A mouse aims at the half it wants; a controller has no cursor, so pressing A on
    /// the switch has to mean "the other one" - which is unambiguous with exactly two positions.
    /// Does nothing without a VR build, matching the switch being disabled in that case.
    /// </summary>
    [RelayCommand]
    private void ToggleLaunchMode()
    {
        if (HasVr)
        {
            IsVrMode = !IsVrMode;
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        // An explicit action, not "still typing" - skips the debounce so the grid updates immediately.
        _searchDebounceTimer.Stop();
        SearchText = string.Empty;
        TablesView.Refresh();
        UpdateVisibleCount();
    }

    /// <summary>Full paths of every table currently starred, across all installations - the persisted form of every tile's IsFavorite.</summary>
    private HashSet<string> _favoriteTablePaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cached copy of NudgeSettings.TableCustomizations, refreshed in LoadPreferencesAsync - applied to each tile as it's created (ApplyTables) so a custom image survives a rescan/restart without waiting on IArtworkProvider.</summary>
    private Dictionary<string, TableCustomization> _tableCustomizations = new(StringComparer.OrdinalIgnoreCase);

    [RelayCommand]
    private void ToggleFavorite(TableTileViewModel? tile)
    {
        if (tile is null)
        {
            return;
        }

        // Deferred to the next dispatcher pass, not applied inline: while "Favourites only" is
        // selected, flipping IsFavorite here live-filters the tile's own container out of the grid
        // immediately (see IsLiveFiltering in the constructor) - but WPF's routed Click event that
        // got us into this handler is still unwinding through that very Button. Pulling its
        // container out from under it mid-click is what was throwing. Running the mutation after
        // Click finishes dispatching sidesteps that without giving up the live update.
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            tile.IsFavorite = !tile.IsFavorite;

            if (tile.IsFavorite)
            {
                _favoriteTablePaths.Add(tile.Table.Path);
            }
            else
            {
                _favoriteTablePaths.Remove(tile.Table.Path);
            }

            _ = SaveFavoritesAsync();
        });
    }

    private async Task SaveFavoritesAsync()
    {
        try
        {
            NudgeSettings settings = await _settingsService.LoadAsync().ConfigureAwait(true);
            settings.FavoriteTablePaths = _favoriteTablePaths.ToList();
            await _settingsService.SaveAsync(settings).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The star already toggled on screen; failing to remember it isn't worth an error box.
            _logger.LogWarning(ex, "Could not save favourites.");
        }
    }

    /// <summary>
    /// Runs once the setup screen confirms an installation. Shows whatever is already in the
    /// database immediately (fast - it's a local read), then scans in the background and refreshes,
    /// so the grid is never blocked on a full rescan before it shows anything.
    /// </summary>
    public async Task ActivateAsync(VpxInstallation installation)
    {
        _installation = installation;
        InstallationDisplayName = installation.DisplayName;
        HasVr = installation.BestVrExecutable is not null;
        StatusMessage = string.Empty;

        // LoadPreferences first: it restores TablesPathOverride, which EffectiveTablesPath below
        // depends on. Scanning before it would scan the detected folder even when the user has
        // explicitly pointed Nudge somewhere else.
        await LoadPreferencesAsync().ConfigureAwait(true);
        await LoadFromDatabaseAsync().ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(EffectiveTablesPath))
        {
            await ScanAsync().ConfigureAwait(true);

            // Without this, adding or removing a .vpx file only gets picked up the next time the
            // app starts or the user remembers to click "Rescan" - see ITableFolderWatcher's own
            // remarks. StartWatchingTablesFolder also disposes any previous watch, which matters
            // because ActivateAsync runs again whenever the confirmed installation changes.
            StartWatchingTablesFolder();

            // Fire-and-forget, well before anyone hovers anything: the trailer index is several
            // megabytes, and paying for it on the first hover means that hover shows nothing at all.
            // Gated on the same internet consent every other network lookup uses.
            if (FetchArtworkFromInternet)
            {
                _ = _tableTrailerProvider.WarmUpAsync();
            }
        }
        else
        {
            _folderWatch?.Dispose();
            _folderWatch = null;
            StatusMessage = "Nudge couldn't find a Tables folder. Set one in Settings under \"Tables folder\".";
        }
    }

    /// <summary>Stops watching the Tables folder. Called when the host container disposes this singleton on app shutdown.</summary>
    public void Dispose() => _folderWatch?.Dispose();

    /// <summary>
    /// What each button does in a table, in the order they matter to a player - flippers first,
    /// housekeeping last. Also fixes the row order, which is why it is a list rather than a
    /// dictionary over the enum's own declaration order.
    /// </summary>
    private static readonly (ControllerButton Button, string Role)[] ControllerRoles =
    [
        (ControllerButton.LeftShoulder, "Left flipper"),
        (ControllerButton.RightShoulder, "Right flipper"),
        (ControllerButton.LeftTrigger, "Left MagnaSave"),
        (ControllerButton.RightTrigger, "Right MagnaSave"),
        (ControllerButton.A, "Plunger / launch"),
        (ControllerButton.DPadUp, "Nudge forward"),
        (ControllerButton.DPadLeft, "Nudge left"),
        (ControllerButton.DPadRight, "Nudge right"),
        // Not "Nudge back": Visual Pinball has no standard key for nudging backwards (Space, Z and /
        // are forward, left and right), so the default mapping leaves this button alone and the row
        // would forever read "Not assigned" against a promise of a function that does not exist.
        (ControllerButton.DPadDown, "Unassigned"),
        (ControllerButton.Start, "Start game"),
        (ControllerButton.Back, "Insert coin"),
        (ControllerButton.Y, "Table menu / exit"),
        (ControllerButton.B, "Unassigned"),
        (ControllerButton.X, "Unassigned"),
        (ControllerButton.LeftThumb, "Unassigned"),
        (ControllerButton.RightThumb, "Unassigned")
    ];

    /// <summary>
    /// Builds the binding rows from the saved overrides. Called when the settings screen opens.
    ///
    /// No longer polls the pad. The rows used to light up as buttons were pressed, which was a
    /// genuinely useful way to identify a button - but once the pad also navigates this page, the
    /// same press both moves the focus and lights a lamp somewhere else, and the two readings of one
    /// button contradict each other. Navigation is the more valuable of the two, so the lamps go.
    /// </summary>
    public void BeginControllerSetup()
    {
        ControllerMapping mapping = ControllerMapping.FromOverrides(_controllerButtonOverrides);

        ControllerBindings.Clear();
        foreach ((ControllerButton button, string role) in ControllerRoles)
        {
            ControllerBindings.Add(new ControllerBindingViewModel(button, role)
            {
                Key = mapping.TryGetKey(button)
            });
        }

        ControllerHint = string.Empty;
    }

    /// <summary>Clears any half-finished rebind when the settings screen closes.</summary>
    public void EndControllerSetup()
    {
        foreach (ControllerBindingViewModel binding in ControllerBindings)
        {
            binding.IsListening = false;
        }

        OnPropertyChanged(nameof(IsRebinding));
    }

    /// <summary>Puts one row into "press a key" mode. Only ever one at a time, so a stray press can't land on two rows.</summary>
    [RelayCommand]
    private void StartRebind(ControllerBindingViewModel? binding)
    {
        if (binding is null)
        {
            return;
        }

        foreach (ControllerBindingViewModel other in ControllerBindings)
        {
            other.IsListening = ReferenceEquals(other, binding);
        }

        ControllerHint = $"Press the key you want {binding.ButtonLabel} to send. Esc cancels.";
        OnPropertyChanged(nameof(IsRebinding));
    }

    /// <summary>
    /// Applies a captured key to whichever row is listening. Returns true when the press was
    /// consumed, so the view can mark the key event handled and stop it reaching anything else.
    /// </summary>
    public bool ApplyCapturedKey(Key key)
    {
        ControllerBindingViewModel? listening = ControllerBindings.FirstOrDefault(b => b.IsListening);
        if (listening is null)
        {
            return false;
        }

        // Escape cancels rather than binding, since a row bound to Escape by a mis-press would be
        // awkward to undo and Escape is the conventional "back out" key.
        if (key == Key.Escape)
        {
            listening.IsListening = false;
            ControllerHint = string.Empty;
            OnPropertyChanged(nameof(IsRebinding));
            return true;
        }

        VirtualKey? captured = KeyCapture.FromWpfKey(key);
        if (captured is null)
        {
            // Stays in listening mode so the next press can succeed - the alternative is silently
            // cancelling and leaving the user unsure whether anything happened.
            ControllerHint = "Nudge can't send that key to Visual Pinball. Try another one, or press Esc to cancel.";
            return true;
        }

        listening.Key = captured;
        listening.IsListening = false;
        ControllerHint = string.Empty;
        OnPropertyChanged(nameof(IsRebinding));

        _controllerButtonOverrides[listening.Button.ToString()] = captured.Value.ToString();
        _ = PersistControllerBindingsAsync();
        return true;
    }

    /// <summary>Puts every button back to the Visual Pinball defaults.</summary>
    [RelayCommand]
    private void ResetControllerBindings()
    {
        _controllerButtonOverrides.Clear();
        _ = PersistControllerBindingsAsync();
        BeginControllerSetup();
    }

    private async Task PersistControllerBindingsAsync()
    {
        try
        {
            await _settingsService.MutateAsync(s =>
                s.ControllerButtonOverrides = new Dictionary<string, string>(_controllerButtonOverrides))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save controller bindings.");
        }
    }

    /// <summary>
    /// The video to play while <paramref name="tile"/> is hovered, or null if it hasn't got one.
    ///
    /// A video the user assigned by hand on the customization page always wins; otherwise the
    /// locator looks for one already on disk under the layouts the common frontends use. Resolved
    /// lazily on first hover rather than for every table up front: a library of a thousand tables
    /// would otherwise do thousands of file probes at startup to answer a question about tiles
    /// nobody may ever point at. The answer is cached onto the tile (including "nothing found", via
    /// <see cref="TableTileViewModel.VideoLookupDone"/>) so the probe happens at most once per tile.
    /// </summary>
    public string? ResolveHoverVideo(TableTileViewModel tile)
    {
        if (!string.IsNullOrWhiteSpace(tile.VideoPath))
        {
            return tile.VideoPath;
        }

        if (tile.VideoLookupDone)
        {
            return null;
        }

        tile.VideoLookupDone = true;
        tile.VideoPath = _tableVideoLocator.Locate(tile.Table.Path, _installation?.RootPath, tile.DisplayTitle);
        return tile.VideoPath;
    }

    /// <summary>
    /// The YouTube video id for this table's online preview, or null when there isn't one.
    ///
    /// Only consulted once <see cref="ResolveHoverVideo"/> has found no local file, and gated on the
    /// same "fetch from the internet" consent the artwork lookup uses - this is a network call, and
    /// a user who has turned that off has said they don't want Nudge reaching out at all.
    /// </summary>
    public async Task<string?> ResolveOnlineTrailerAsync(TableTileViewModel tile)
    {
        if (!FetchArtworkFromInternet)
        {
            return null;
        }

        // An explicitly chosen video wins outright, and needs no lookup or network call at all - the
        // user already answered this question on the customization page.
        if (!string.IsNullOrWhiteSpace(tile.TrailerYouTubeId))
        {
            return tile.TrailerYouTubeId;
        }

        try
        {
            return await _tableTrailerProvider.GetYouTubeVideoIdAsync(tile.Table).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve an online trailer.");
            return null;
        }
    }

    /// <summary>
    /// The folder actually scanned: the user's explicit override when they have set one, otherwise
    /// whatever the installation's own detection found. Every scan and the folder watcher both go
    /// through here, so the two can never end up pointed at different folders.
    /// </summary>
    private string? EffectiveTablesPath =>
        string.IsNullOrWhiteSpace(TablesPathOverride) ? _installation?.TablesPath : TablesPathOverride;

    /// <summary>An explicit tables folder, or blank to use the one detected from the installation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTablesPathOverride))]
    [NotifyPropertyChangedFor(nameof(EffectiveTablesPathDisplay))]
    private string _tablesPathOverride = string.Empty;

    public bool HasTablesPathOverride => !string.IsNullOrWhiteSpace(TablesPathOverride);

    /// <summary>What the settings row shows, so it is always obvious which folder is really in use.</summary>
    public string EffectiveTablesPathDisplay =>
        EffectiveTablesPath ?? "No tables folder found for this installation.";

    [RelayCommand]
    private async Task BrowseTablesFolderAsync()
    {
        string? chosen = _folderPickerService.PickFolder(
            "Choose the folder Nudge should scan for .vpx tables",
            EffectiveTablesPath);

        if (string.IsNullOrWhiteSpace(chosen))
        {
            return;
        }

        TablesPathOverride = chosen;
        await ApplyTablesPathChangeAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearTablesFolderAsync()
    {
        TablesPathOverride = string.Empty;
        await ApplyTablesPathChangeAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Persists the new folder, re-points the watcher at it, and rescans - all three, because a
    /// changed folder that is saved but neither watched nor scanned looks exactly like the "Nudge
    /// isn't picking up my tables" bug this setting exists to solve.
    /// </summary>
    private async Task ApplyTablesPathChangeAsync()
    {
        await _settingsService.MutateAsync(s =>
            s.TablesPathOverride = string.IsNullOrWhiteSpace(TablesPathOverride) ? null : TablesPathOverride)
            .ConfigureAwait(true);

        OnPropertyChanged(nameof(EffectiveTablesPathDisplay));
        StartWatchingTablesFolder();
        await ScanAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// (Re)starts the live folder watch against <see cref="EffectiveTablesPath"/>. Always disposes
    /// the previous watch first, so changing folders never leaves an old one firing rescans for a
    /// directory that is no longer the active library.
    /// </summary>
    private void StartWatchingTablesFolder()
    {
        _folderWatch?.Dispose();
        _folderWatch = null;

        string? path = EffectiveTablesPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // Application.Current.Dispatcher, not Dispatcher.CurrentDispatcher: the latter returns the
        // dispatcher for whatever thread happens to call this, and creates a brand-new (never-pumped)
        // one if that thread has none. Queued rescans would then sit forever on a dispatcher nothing
        // is running - the watcher would look wired up and fire correctly, yet the grid would
        // silently never refresh.
        Dispatcher uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _folderWatch = _tableFolderWatcher.Watch(path, () =>
        {
            uiDispatcher.InvokeAsync(() => _ = ScanAsync());
            return Task.CompletedTask;
        });
    }

    private async Task LoadPreferencesAsync()
    {
        _isLoadingPreferences = true;
        try
        {
            NudgeSettings settings = await _settingsService.LoadAsync().ConfigureAwait(true);

            SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == _themeService.Parse(settings.ThemeName))
                            ?? ThemeOptions[0];
            SelectedUiStyle = UiStyleOptions.FirstOrDefault(o => o.Value == _uiStyleService.Parse(settings.UiStyleName))
                              ?? UiStyleOptions[0];
            TablesPathOverride = settings.TablesPathOverride ?? string.Empty;
            SelectedSort = SortOptions.FirstOrDefault(o => o.Value.ToString() == settings.SortOrder)
                           ?? SortOptions[0];
            ShowConfidence = settings.ShowConfidence;
            FetchArtworkFromInternet = settings.FetchArtworkFromInternet;
            SelectedArtworkSource = ArtworkSourceOptions.FirstOrDefault(o => o.Value == settings.DefaultArtworkSourceName)
                                     ?? ArtworkSourceOptions[0];
            GoogleCustomSearchApiKey = settings.GoogleCustomSearchApiKey ?? string.Empty;
            GoogleCustomSearchEngineId = settings.GoogleCustomSearchEngineId ?? string.Empty;
            TintTableBanner = settings.TintTableBanner;
            _controllerButtonOverrides = new Dictionary<string, string>(settings.ControllerButtonOverrides, StringComparer.OrdinalIgnoreCase);
            EnableMediaTrailers = settings.EnableMediaTrailers;
            MuteMediaTrailers = settings.MuteMediaTrailers;
            _favoriteTablePaths = new HashSet<string>(settings.FavoriteTablePaths, StringComparer.OrdinalIgnoreCase);
            _tableCustomizations = new Dictionary<string, TableCustomization>(settings.TableCustomizations, StringComparer.OrdinalIgnoreCase);
            TablesPerRow = Math.Clamp(settings.TablesPerRow, 3, 8);
            EnableShadows = settings.EnableShadows;
            ShadowIntensityPercent = Math.Clamp(settings.ShadowIntensityPercent, 25, 175);
            SelectedLayoutMode = LayoutModeOptions.FirstOrDefault(o =>
                                     o.Value.ToString() == settings.LayoutMode)
                                 ?? LayoutModeOptions[0];

            // Only honour a saved VR preference if this installation can actually do VR.
            IsVrMode = settings.PreferVr && HasVr;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load display preferences; falling back to defaults.");
        }
        finally
        {
            _isLoadingPreferences = false;
        }
    }

    private async Task SavePreferencesAsync()
    {
        if (_isLoadingPreferences)
        {
            return;
        }

        try
        {
            // MutateAsync, not a separate LoadAsync+SaveAsync - this fires from a dozen different
            // property-changed handlers (theme, sort, every toggle, both artwork-source text fields
            // as the user types...), any two of which firing close together used to be able to race:
            // both load the same on-disk version, both compute their own updated copy, and whichever
            // finishes saving second would silently discard the first's change. See ISettingsService's
            // own remarks on why MutateAsync closes that gap.
            await _settingsService.MutateAsync(settings =>
            {
                settings.ThemeName = (SelectedTheme?.Value ?? AppTheme.Dark).ToString();
                settings.UiStyleName = (SelectedUiStyle?.Value ?? AppUiStyle.Pin).ToString();
                settings.SortOrder = (SelectedSort?.Value ?? TableSortOrder.TitleAscending).ToString();
                settings.ShowConfidence = ShowConfidence;
                settings.FetchArtworkFromInternet = FetchArtworkFromInternet;
                settings.DefaultArtworkSourceName = SelectedArtworkSource?.Value ?? "vps-db";
                settings.GoogleCustomSearchApiKey = string.IsNullOrWhiteSpace(GoogleCustomSearchApiKey) ? null : GoogleCustomSearchApiKey;
                settings.GoogleCustomSearchEngineId = string.IsNullOrWhiteSpace(GoogleCustomSearchEngineId) ? null : GoogleCustomSearchEngineId;
                settings.TintTableBanner = TintTableBanner;
                settings.EnableMediaTrailers = EnableMediaTrailers;
                settings.MuteMediaTrailers = MuteMediaTrailers;
                settings.PreferVr = IsVrMode;
                settings.TablesPerRow = TablesPerRow;
                settings.EnableShadows = EnableShadows;
                settings.ShadowIntensityPercent = ShadowIntensityPercent;
                settings.LayoutMode = (SelectedLayoutMode?.Value ?? LibraryLayoutMode.Grid).ToString();
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The preference already applied on screen; failing to remember it isn't worth an error box.
            _logger.LogWarning(ex, "Could not save display preferences.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async Task RescanAsync() => await ScanAsync().ConfigureAwait(true);

    private bool CanRescan() => !IsScanning && !string.IsNullOrWhiteSpace(EffectiveTablesPath);

    [ObservableProperty]
    private bool _isFindingArtwork;

    /// <summary>
    /// The explicit, on-demand alternative to waiting for artwork to load tile-by-tile as you happen
    /// to scroll past each one: asks IArtworkProvider for every table that doesn't have artwork yet,
    /// all at once, rather than only the handful currently on screen. Bounded to a handful of
    /// concurrent lookups (MaxDegreeOfParallelism) rather than firing all of them at once - a library
    /// of 1,000 tables would otherwise open 1,000 simultaneous network/disk requests, which helps
    /// nobody. Reuses TableTileViewModel.EnsureArtworkLoadedAsync, so a tile already mid-fetch from
    /// ordinary scrolling is not asked twice.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFindMissingArtwork))]
    private async Task FindMissingArtworkAsync()
    {
        List<TableTileViewModel> missing = Tables.Where(t => !t.HasArtwork).ToList();
        if (missing.Count == 0)
        {
            StatusMessage = "Every table already has artwork.";
            return;
        }

        IsFindingArtwork = true;
        FindMissingArtworkCommand.NotifyCanExecuteChanged();
        StatusMessage = $"Finding artwork for {missing.Count} table{(missing.Count == 1 ? string.Empty : "s")}…";

        try
        {
            await Parallel.ForEachAsync(
                missing,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (tile, cancellationToken) => await tile.EnsureArtworkLoadedAsync(cancellationToken).ConfigureAwait(false))
                .ConfigureAwait(true);

            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Finding missing artwork failed partway through.");
            StatusMessage = "Nudge couldn't finish finding artwork. The log file has the details.";
        }
        finally
        {
            IsFindingArtwork = false;
            FindMissingArtworkCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanFindMissingArtwork() => !IsFindingArtwork && FetchArtworkFromInternet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatusLine))]
    private bool _isLaunching;

    /// <summary>
    /// Launches a table and waits for Visual Pinball to exit - the "click a tile, play, VPX exits,
    /// back to the library" core loop from AGENTS.md section 1. Which build runs depends on the
    /// 2D/VR switch: VR mode uses the installation's VR-capable executable, relying on Visual
    /// Pinball's own autodetection of an active headset (AGENTS.md section 4.3).
    /// </summary>
    /// <remarks>
    /// This deliberately does not try to hold Nudge in the foreground or hand focus back to Visual
    /// Pinball once it appears - an earlier version did, with a fixed 12-second delay standing in
    /// for "the table finished loading" (which Visual Pinball has no way to report). Investigated
    /// whether any VPX frontend has a real signal for that: none do. PinUP Popper, a mature,
    /// long-established frontend, has open, years-old bugs and workarounds around this exact
    /// problem (window-detection timing mismatches, `-Minimized` flag quirks); no frontend
    /// examined has a clean answer, only guesses of varying quality. Rather than ship another
    /// guess, Visual Pinball's own process simply takes focus immediately, the same as launching
    /// any other application - this overlay just stays up behind it for the whole play session.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchTableAsync(TableTileViewModel? tile)
    {
        if (tile is null || _installation is null || IsLaunching)
        {
            return;
        }

        bool useVr = IsVrMode && HasVr;
        VpxExecutable? vrExecutable = useVr ? _installation.BestVrExecutable : null;

        IsLaunching = true;
        LaunchTableCommand.NotifyCanExecuteChanged();
        StatusMessage = useVr
            ? $"Launching {tile.DisplayTitle} in VR…"
            : $"Launching {tile.DisplayTitle}…";

        try
        {
            Result<LaunchOutcome> result = vrExecutable is not null
                ? await _launchEngine.LaunchAsync(vrExecutable, tile.Table.Path).ConfigureAwait(true)
                : await _launchEngine.LaunchAsync(_installation, tile.Table.Path).ConfigureAwait(true);

            if (result.IsFailure)
            {
                StatusMessage = result.Error;
                return;
            }

            StatusMessage = string.Empty;
            _logger.LogInformation(
                "Launched {Path} via {Executable}, exit code {ExitCode}, played for {Duration}.",
                _redactor.Redact(tile.Table.Path),
                _redactor.Redact(result.Value.ExecutablePath),
                result.Value.ExitCode,
                result.Value.Duration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Launching {Path} failed.", _redactor.Redact(tile.Table.Path));
            StatusMessage = "Nudge could not launch that table. The log file has the details.";
        }
        finally
        {
            IsLaunching = false;
            LaunchTableCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanLaunch(TableTileViewModel? tile) => !IsLaunching && _installation is not null;

    private async Task ScanAsync()
    {
        // EffectiveTablesPath, not _installation.TablesPath: an explicit override in Settings has to
        // win here, or the scan and the folder watcher end up pointed at two different directories.
        string? tablesPath = EffectiveTablesPath;
        if (_installation is null || string.IsNullOrWhiteSpace(tablesPath) || IsScanning)
        {
            return;
        }

        IsScanning = true;
        RescanCommand.NotifyCanExecuteChanged();
        StatusMessage = "Scanning your tables…";
        ScanProgressPercent = 0;

        // Progress<T> captures the current SynchronizationContext at construction and marshals every
        // Report() back to it via Post - queued, not synchronous, even when called from the UI thread
        // itself. That queueing is exactly why this only ever touches ScanProgressPercent, never
        // StatusMessage: a scan where every file is already unchanged runs the scanner's whole loop
        // synchronously with no real await inside it, so all of its Report() calls end up queued
        // behind (not before) ScanAsync's own "scan finished, clear the message" line below - a
        // StatusMessage set from in here would win that race and get stuck reading "Scanning 61 of
        // 61…" forever. ScanProgressPercent has no such problem: once IsScanning flips false the bar
        // is hidden (IsRescanning), so a stray queued update after that point is simply never seen.
        var progress = new Progress<ScanProgress>(p =>
        {
            ScanProgressPercent = p.Total > 0 ? (double)p.Completed / p.Total * 100 : 0;
        });

        try
        {
            ScanResult result = await _scanner
                .ScanAsync(_installation.Id, tablesPath, progress)
                .ConfigureAwait(true);

            await LoadFromDatabaseAsync().ConfigureAwait(true);

            StatusMessage = result.Failed > 0
                ? $"{result.Failed} table{(result.Failed == 1 ? string.Empty : "s")} could not be read. The log file has the details."
                : string.Empty;

            _logger.LogInformation(
                "Library scan for {Id} finished in {ElapsedMs} ms: {Scanned} scanned, {Skipped} unchanged, "
                + "{Failed} failed, {Removed} removed.",
                _installation.Id,
                result.Duration.TotalMilliseconds,
                result.Scanned,
                result.Skipped,
                result.Failed,
                result.Removed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scanning {Path} failed.", _redactor.Redact(tablesPath));
            StatusMessage = "Nudge could not finish scanning. The log file has the details.";
        }
        finally
        {
            IsScanning = false;
            RescanCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task LoadFromDatabaseAsync()
    {
        if (_installation is null)
        {
            return;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        ITableRepository repository = scope.ServiceProvider.GetRequiredService<ITableRepository>();
        IReadOnlyList<VpxTableFile> tables = await repository
            .GetAllAsync(_installation.Id)
            .ConfigureAwait(true);

        ApplyTables(tables);
    }

    private void ApplyTables(IReadOnlyList<VpxTableFile> tables)
    {
        List<TableTileViewModel> tiles = tables
            .Select(t => new TableTileViewModel(t, _artworkProvider, _logger) { IsFavorite = _favoriteTablePaths.Contains(t.Path) })
            .ToList();

        // A custom image, once saved, always wins over IArtworkProvider - applied immediately here
        // (a fast local file read) rather than waiting on the same lazy, on-scroll loading real
        // artwork uses, since there's no network/disk-cache round trip to defer. The hover video path
        // rides along the same way; nothing loads it until a tile is actually hovered.
        foreach (TableTileViewModel tile in tiles)
        {
            if (!_tableCustomizations.TryGetValue(tile.Table.Path, out TableCustomization? customization))
            {
                // No saved preference for this table, so the test-fixture heuristic decides. Physics
                // rigs and calibration benches ("...Elasticity_Test.vpx") scan and identify fine but
                // aren't tables anyone wants in a library. Hidden rather than dropped from the list:
                // they still appear under Settings' hidden-tables section, so a false positive is
                // always one click from being brought back.
                tile.IsHidden = TestTableHeuristics.LooksLikeTestFixture(tile.Table.DisplayTitle, tile.Table.FileName);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(customization.CustomImagePath))
            {
                tile.SetCustomArtwork(customization.CustomImagePath);
            }

            tile.VideoPath = customization.VideoPath;
            tile.TrailerYouTubeId = customization.TrailerYouTubeId;
            tile.CustomTitle = customization.CustomTitle;
            tile.CustomAuthor = customization.CustomAuthor;
            tile.CustomDate = customization.CustomDate;

            // An explicit saved choice always beats the heuristic - including "false". Someone who
            // un-hides a table Nudge auto-hid must not have it hidden again on the next scan, which
            // is exactly what re-running the heuristic here would do.
            tile.IsHidden = customization.IsHidden;
        }

        // Cleared and re-added rather than reassigning the collection instance, so the grid's
        // TablesView (bound once in the constructor) keeps working without re-subscribing. Ordering
        // is TablesView.CustomSort's job, not this method's.
        Tables.Clear();
        foreach (TableTileViewModel tile in tiles)
        {
            Tables.Add(tile);
        }

        TableCount = Tables.Count;
        RefreshHiddenTables();
        UpdateVisibleCount();
    }

    /// <summary>
    /// Every currently-hidden table, for Settings' own "hidden tables" list - a separate collection
    /// rather than something Settings filters out of Tables itself, since Tables is already filtered
    /// the other way (hidden tiles removed) via FilterTable/TablesView. Rebuilt wholesale on every
    /// ApplyTables and UnhideTable, rather than trying to keep it incrementally in sync - the list is
    /// only ever as large as however many tables someone has actually chosen to hide, so a full
    /// rebuild is never expensive.
    /// </summary>
    public ObservableCollection<TableTileViewModel> HiddenTables { get; } = [];

    /// <summary>Drives Settings' "no tables are hidden" caption vs the actual list - ObservableCollection itself has no bindable Count/empty notification, so this is raised by hand from RefreshHiddenTables.</summary>
    public bool HasHiddenTables => HiddenTables.Count > 0;

    private void RefreshHiddenTables()
    {
        HiddenTables.Clear();
        foreach (TableTileViewModel tile in Tables.Where(t => t.IsHidden))
        {
            HiddenTables.Add(tile);
        }

        OnPropertyChanged(nameof(HasHiddenTables));
    }

    /// <summary>
    /// Keeps this view model's own _tableCustomizations snapshot in step with a save
    /// TableCustomizationViewModel just made directly to disk - called from its SaveAsync rather than
    /// this view model re-reading the whole settings file back, since it already has the exact
    /// TableCustomization (or null, for "cleared back to blank") that was just written.
    /// </summary>
    public void RefreshTableCustomization(TableTileViewModel tile, TableCustomization? customization)
    {
        if (customization is null)
        {
            _tableCustomizations.Remove(tile.Table.Path);
        }
        else
        {
            _tableCustomizations[tile.Table.Path] = customization;
        }

        RefreshHiddenTables();
    }

    /// <summary>
    /// Un-hides a table from Settings' hidden-tables list - the only way back, since a hidden table
    /// never appears in the grid/carousel/list for its own customization page's toggle to reach.
    /// Loads a fresh copy of settings and mutates only this one table's entry rather than writing
    /// back this view model's own (possibly stale - see LoadPreferencesAsync's remarks)
    /// _tableCustomizations snapshot wholesale, which could otherwise clobber a more recent edit
    /// TableCustomizationViewModel.SaveAsync made to some other table in the meantime.
    /// </summary>
    [RelayCommand]
    private void UnhideTable(TableTileViewModel? tile)
    {
        if (tile is null)
        {
            return;
        }

        tile.IsHidden = false;
        if (_tableCustomizations.TryGetValue(tile.Table.Path, out TableCustomization? customization))
        {
            customization.IsHidden = false;
        }

        RefreshHiddenTables();
        _ = PersistUnhideAsync(tile.Table.Path);
    }

    private async Task PersistUnhideAsync(string tablePath)
    {
        await _settingsService.MutateAsync(settings =>
        {
            // Creates the entry when one doesn't exist yet, rather than only updating an existing
            // one. A table auto-hidden by the test-fixture heuristic has no saved customization at
            // all, so the update-only version silently did nothing for exactly the tables most
            // likely to need un-hiding - they came straight back on the next scan.
            if (!settings.TableCustomizations.TryGetValue(tablePath, out TableCustomization? customization))
            {
                customization = new TableCustomization();
                settings.TableCustomizations[tablePath] = customization;
            }

            customization.IsHidden = false;
        }).ConfigureAwait(true);

        // Keep this view model's own snapshot in step, so a rescan before the next settings reload
        // sees the explicit "false" and doesn't re-apply the heuristic.
        if (!_tableCustomizations.TryGetValue(tablePath, out TableCustomization? local))
        {
            local = new TableCustomization();
            _tableCustomizations[tablePath] = local;
        }

        local.IsHidden = false;
    }

    private bool FilterTable(object item)
    {
        if (item is not TableTileViewModel tile)
        {
            return false;
        }

        if (tile.IsHidden)
        {
            return false;
        }

        if (SelectedSort?.Value == TableSortOrder.FavoritesOnly && !tile.IsFavorite)
        {
            return false;
        }

        if (!HasSearchText)
        {
            return true;
        }

        return tile.DisplayTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || tile.Subtitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }
}
