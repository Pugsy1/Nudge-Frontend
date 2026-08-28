using System.Collections.ObjectModel;
using System.ComponentModel;
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

/// <summary>One selectable entry in a settings dropdown - a display label paired with its stored value.</summary>
public sealed record OptionItem<T>(string Label, T Value);

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
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVpxLibraryScanner _scanner;
    private readonly ILaunchEngine _launchEngine;
    private readonly IThemeService _themeService;
    private readonly ISettingsService _settingsService;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<LibraryViewModel> _logger;

    private VpxInstallation? _installation;

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
        ILaunchEngine launchEngine,
        SetupViewModel setup,
        IThemeService themeService,
        ISettingsService settingsService,
        IPathRedactor redactor,
        ILogger<LibraryViewModel> logger)
    {
        _scopeFactory = scopeFactory;
        _scanner = scanner;
        _launchEngine = launchEngine;
        _themeService = themeService;
        _settingsService = settingsService;
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

        // Lets the "Favourites first" sort re-order live the instant a star is clicked, instead of
        // needing a full Refresh() (which would also re-run the filter and jar the scroll position).
        TablesView.IsLiveSorting = true;
        TablesView.LiveSortingProperties.Add(nameof(TableTileViewModel.IsFavorite));

        SortOptions =
        [
            new OptionItem<TableSortOrder>("Title  A → Z", TableSortOrder.TitleAscending),
            new OptionItem<TableSortOrder>("Title  Z → A", TableSortOrder.TitleDescending),
            new OptionItem<TableSortOrder>("Year  newest first", TableSortOrder.YearNewest),
            new OptionItem<TableSortOrder>("Year  oldest first", TableSortOrder.YearOldest),
            new OptionItem<TableSortOrder>("Favourites first", TableSortOrder.FavoritesFirst)
        ];
        _selectedSort = SortOptions[0];

        // "Base — Accent", consistently, for every entry - the previous mix of "Graphite  (dark)"
        // and bare "Jade" (padded with a double space to fake alignment in one case, nothing in the
        // other) read as visually uneven in the dropdown.
        ThemeOptions =
        [
            new OptionItem<AppTheme>("Dark — Amber", AppTheme.Dark),
            new OptionItem<AppTheme>("Light — Amber", AppTheme.Light),
            new OptionItem<AppTheme>("Dark — Jade", AppTheme.Jade),
            new OptionItem<AppTheme>("Dark — Sapphire", AppTheme.Sapphire),
            new OptionItem<AppTheme>("Dark — Crimson", AppTheme.Crimson),
            new OptionItem<AppTheme>("Dark — Chrome", AppTheme.Chrome),
            new OptionItem<AppTheme>("Dark — Hulk", AppTheme.Hulk)
        ];
        _selectedTheme = ThemeOptions[0];

        ApplySort();
    }

    public ObservableCollection<TableTileViewModel> Tables { get; }

    public ListCollectionView TablesView { get; }

    public IRelayCommand ChangeFolderCommand { get; }

    public IReadOnlyList<OptionItem<TableSortOrder>> SortOptions { get; }

    public IReadOnlyList<OptionItem<AppTheme>> ThemeOptions { get; }

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
    private int _tableCount;

    /// <summary>How many tiles survive the current search filter - drives the "nothing matched" empty state.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSearchResults))]
    [NotifyPropertyChangedFor(nameof(HasVisibleTables))]
    private int _visibleTableCount;

    /// <summary>True while the very first scan (nothing loaded from the database yet) is in flight.</summary>
    public bool IsInitialScanning => IsScanning && TableCount == 0;

    /// <summary>True once a scan has actually run and the installation genuinely holds no tables.</summary>
    public bool HasNoTables => !IsScanning && TableCount == 0;

    public bool HasTables => TableCount > 0;

    /// <summary>The library has tables, but the current search matched none of them.</summary>
    public bool HasNoSearchResults => TableCount > 0 && VisibleTableCount == 0 && HasSearchText;

    public bool HasVisibleTables => VisibleTableCount > 0;

    // ---------------------------------------------------------------- Preferences

    [ObservableProperty]
    private OptionItem<TableSortOrder> _selectedSort;

    [ObservableProperty]
    private OptionItem<AppTheme> _selectedTheme;

    /// <summary>Shows the per-tile identification-confidence lamp. Off by default - the data has always been computed, but it is diagnostic detail most of the time.</summary>
    [ObservableProperty]
    private bool _showConfidence;

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

    [ObservableProperty]
    private bool _isSettingsOpen;

    public string ThemeToggleLabel => _themeService.Current == AppTheme.Dark
        ? "Switch to light theme"
        : "Switch to dark theme";

    partial void OnSearchTextChanged(string value)
    {
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
        _ = SavePreferencesAsync();
    }

    partial void OnShowConfidenceChanged(bool value) => _ = SavePreferencesAsync();

    partial void OnIsVrModeChanged(bool value) => _ = SavePreferencesAsync();

    private void ApplySort()
    {
        TablesView.CustomSort = SelectedSort?.Value switch
        {
            TableSortOrder.TitleDescending => new TableTitleComparer(ascending: false),
            TableSortOrder.YearNewest => new TableYearComparer(newestFirst: true),
            TableSortOrder.YearOldest => new TableYearComparer(newestFirst: false),
            TableSortOrder.FavoritesFirst => new TableFavoriteComparer(),
            _ => new TableTitleComparer(ascending: true)
        };

        UpdateVisibleCount();
    }

    private void UpdateVisibleCount() => VisibleTableCount = TablesView.Count;

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private void SetLaunchMode(string mode) => IsVrMode = mode == "Vr";

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

    [RelayCommand]
    private void ToggleFavorite(TableTileViewModel? tile)
    {
        if (tile is null)
        {
            return;
        }

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

        await LoadPreferencesAsync().ConfigureAwait(true);
        await LoadFromDatabaseAsync().ConfigureAwait(true);

        if (installation.HasTablesFolder)
        {
            await ScanAsync().ConfigureAwait(true);
        }
        else
        {
            StatusMessage = "Nudge couldn't find a Tables folder for this installation.";
        }
    }

    private async Task LoadPreferencesAsync()
    {
        _isLoadingPreferences = true;
        try
        {
            NudgeSettings settings = await _settingsService.LoadAsync().ConfigureAwait(true);

            SelectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == _themeService.Parse(settings.ThemeName))
                            ?? ThemeOptions[0];
            SelectedSort = SortOptions.FirstOrDefault(o => o.Value.ToString() == settings.SortOrder)
                           ?? SortOptions[0];
            ShowConfidence = settings.ShowConfidence;
            _favoriteTablePaths = new HashSet<string>(settings.FavoriteTablePaths, StringComparer.OrdinalIgnoreCase);

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
            NudgeSettings settings = await _settingsService.LoadAsync().ConfigureAwait(true);
            settings.ThemeName = (SelectedTheme?.Value ?? AppTheme.Dark).ToString();
            settings.SortOrder = (SelectedSort?.Value ?? TableSortOrder.TitleAscending).ToString();
            settings.ShowConfidence = ShowConfidence;
            settings.PreferVr = IsVrMode;
            await _settingsService.SaveAsync(settings).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The preference already applied on screen; failing to remember it isn't worth an error box.
            _logger.LogWarning(ex, "Could not save display preferences.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async Task RescanAsync() => await ScanAsync().ConfigureAwait(true);

    private bool CanRescan() => !IsScanning && _installation?.HasTablesFolder == true;

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
        if (_installation?.TablesPath is null || IsScanning)
        {
            return;
        }

        IsScanning = true;
        RescanCommand.NotifyCanExecuteChanged();
        StatusMessage = "Scanning your tables…";

        try
        {
            ScanResult result = await _scanner
                .ScanAsync(_installation.Id, _installation.TablesPath)
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
            _logger.LogError(ex, "Scanning {Path} failed.", _redactor.Redact(_installation.TablesPath));
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
            .Select(t => new TableTileViewModel(t) { IsFavorite = _favoriteTablePaths.Contains(t.Path) })
            .ToList();

        // Cleared and re-added rather than reassigning the collection instance, so the grid's
        // TablesView (bound once in the constructor) keeps working without re-subscribing. Ordering
        // is TablesView.CustomSort's job, not this method's.
        Tables.Clear();
        foreach (TableTileViewModel tile in tiles)
        {
            Tables.Add(tile);
        }

        TableCount = Tables.Count;
        UpdateVisibleCount();
    }

    private bool FilterTable(object item)
    {
        if (!HasSearchText)
        {
            return true;
        }

        return item is TableTileViewModel tile
               && (tile.DisplayTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                   || tile.Subtitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    }
}
