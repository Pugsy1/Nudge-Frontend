using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nudge.App.Services;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;

namespace Nudge.App.ViewModels;

/// <summary>
/// The library screen: a virtualized grid of the tables scanned for the confirmed installation,
/// with a search box.
/// </summary>
/// <remarks>
/// <see cref="ITableRepository"/> and its <c>NudgeDbContext</c> are registered Scoped (EF Core's
/// default), so this resolves them through a fresh <see cref="IServiceScopeFactory"/> scope per
/// operation rather than holding one for the view model's whole lifetime - see AGENTS.md section 5
/// and the note left in Phase 3's <c>Nudge.App.App.xaml.cs</c>.
/// </remarks>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVpxLibraryScanner _scanner;
    private readonly IThemeService _themeService;
    private readonly ISettingsService _settingsService;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<LibraryViewModel> _logger;

    private VpxInstallation? _installation;

    public LibraryViewModel(
        IServiceScopeFactory scopeFactory,
        IVpxLibraryScanner scanner,
        SetupViewModel setup,
        IThemeService themeService,
        ISettingsService settingsService,
        IPathRedactor redactor,
        ILogger<LibraryViewModel> logger)
    {
        _scopeFactory = scopeFactory;
        _scanner = scanner;
        _themeService = themeService;
        _settingsService = settingsService;
        _redactor = redactor;
        _logger = logger;

        // Reuses the setup flow's own "change folder" logic rather than duplicating it - picking a
        // different folder is still fundamentally a setup concern.
        ChangeFolderCommand = setup.ChangeFolderCommand;

        Tables = [];
        TablesView = CollectionViewSource.GetDefaultView(Tables);
        TablesView.Filter = FilterTable;
    }

    public ObservableCollection<TableTileViewModel> Tables { get; }

    public ICollectionView TablesView { get; }

    public IRelayCommand ChangeFolderCommand { get; }

    [ObservableProperty]
    private string _installationDisplayName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    private string _searchText = string.Empty;

    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTables))]
    [NotifyPropertyChangedFor(nameof(IsInitialScanning))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTables))]
    [NotifyPropertyChangedFor(nameof(HasTables))]
    [NotifyPropertyChangedFor(nameof(IsInitialScanning))]
    private int _tableCount;

    /// <summary>True while the very first scan (nothing loaded from the database yet) is in flight - shows a full-screen loading state rather than an empty grid.</summary>
    public bool IsInitialScanning => IsScanning && TableCount == 0;

    /// <summary>True once a scan has actually run and found nothing - not while the first scan is still in flight.</summary>
    public bool HasNoTables => !IsScanning && TableCount == 0;

    public bool HasTables => TableCount > 0;

    public string ThemeToggleLabel => _themeService.Current == AppTheme.Dark
        ? "Switch to light theme"
        : "Switch to dark theme";

    partial void OnSearchTextChanged(string value) => TablesView.Refresh();

    /// <summary>
    /// Runs once the setup screen confirms an installation. Shows whatever is already in the
    /// database immediately (fast - it's a local read), then scans in the background and refreshes,
    /// so the grid is never blocked on a full rescan before it shows anything.
    /// </summary>
    public async Task ActivateAsync(VpxInstallation installation)
    {
        _installation = installation;
        InstallationDisplayName = installation.DisplayName;
        StatusMessage = string.Empty;

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

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async Task RescanAsync() => await ScanAsync().ConfigureAwait(true);

    private bool CanRescan() => !IsScanning && _installation?.HasTablesFolder == true;

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        AppTheme next = _themeService.Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _themeService.Apply(next);
        OnPropertyChanged(nameof(ThemeToggleLabel));

        try
        {
            NudgeSettings settings = await _settingsService.LoadAsync().ConfigureAwait(true);
            settings.ThemeName = next.ToString();
            await _settingsService.SaveAsync(settings).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The theme already changed on screen; failing to remember it is not worth an error box.
            _logger.LogWarning(ex, "Could not save the theme preference.");
        }
    }

    private async Task ScanAsync()
    {
        if (_installation?.TablesPath is null || IsScanning)
        {
            return;
        }

        IsScanning = true;
        RescanCommand.NotifyCanExecuteChanged();
        StatusMessage = "Scanning your tables...";

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
        List<TableTileViewModel> ordered = tables
            .OrderBy(t => t.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .Select(t => new TableTileViewModel(t))
            .ToList();

        // Cleared and re-added rather than reassigning the collection instance, so the grid's
        // TablesView (bound once in the constructor) keeps working without re-subscribing.
        Tables.Clear();
        foreach (TableTileViewModel tile in ordered)
        {
            Tables.Add(tile);
        }

        TableCount = Tables.Count;
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
