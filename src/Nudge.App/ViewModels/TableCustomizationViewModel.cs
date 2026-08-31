using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nudge.App.Services;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;
using Nudge.Core.Results;

namespace Nudge.App.ViewModels;

/// <summary>
/// The per-table customization page: lets the user write a description and how-to-play notes, pick a
/// local image or hand-browse artwork sources for one, pick a local hover video, hide the table, and
/// see whether its PinMAME ROM is present. A fresh instance is created per table
/// (LibraryViewModel.OpenTableCustomization), not a long-lived DI singleton like SettingsViewModel -
/// its content is entirely about whichever one table it was opened for.
/// </summary>
public sealed partial class TableCustomizationViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IFilePickerService _filePickerService;
    private readonly IArtworkBrowser _artworkBrowser;
    private readonly IRomNameReader _romNameReader;
    private readonly IRomAvailabilityChecker _romAvailabilityChecker;
    private readonly ICustomArtworkStore _customArtworkStore;
    private readonly ITableTrailerProvider _tableTrailerProvider;

    public TableCustomizationViewModel(
        TableTileViewModel tile,
        LibraryViewModel library,
        ISettingsService settingsService,
        IFilePickerService filePickerService,
        ICustomArtworkStore customArtworkStore,
        ITableTrailerProvider tableTrailerProvider,
        IArtworkBrowser artworkBrowser,
        IRomNameReader romNameReader,
        IRomAvailabilityChecker romAvailabilityChecker)
    {
        Tile = tile;
        Library = library;
        _settingsService = settingsService;
        _filePickerService = filePickerService;
        _customArtworkStore = customArtworkStore;
        _tableTrailerProvider = tableTrailerProvider;
        _artworkBrowser = artworkBrowser;
        _romNameReader = romNameReader;
        _romAvailabilityChecker = romAvailabilityChecker;

        // "vps-db" first if it's there (it always is), and Google Images only ever offered once its
        // two settings fields are actually filled in - picking it otherwise would just search a
        // source IArtworkBrowser itself reports "not configured" for, which reads as broken rather
        // than as the deliberate gate it is.
        _selectedBrowseSource = ArtworkSourceOptions.FirstOrDefault(o => o.Value == "vps-db")
                                 ?? ArtworkSourceOptions.FirstOrDefault();

        _ = LoadAsync();
        _ = RunRomCheckAsync();
    }

    public TableTileViewModel Tile { get; }

    public LibraryViewModel Library { get; }

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _howToPlay = string.Empty;

    [ObservableProperty]
    private string? _customImagePath;

    /// <summary>Local video played over this table's artwork on hover, when that setting is on - see NudgeSettings.TableCustomization.VideoPath for why this is a picked file rather than something scraped.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VideoFileName))]
    private string? _videoPath;

    /// <summary>Just the file name, for display - a full path is usually too long to read in the card.</summary>
    public string VideoFileName => string.IsNullOrWhiteSpace(VideoPath)
        ? "No video chosen"
        : System.IO.Path.GetFileName(VideoPath);

    [ObservableProperty]
    private bool _isSaving;

    /// <summary>Title override - blank means keep showing Table.DisplayTitle.</summary>
    [ObservableProperty]
    private string _customTitle = string.Empty;

    /// <summary>"Made by" credit override, shown alongside the date in the tile subtitle.</summary>
    [ObservableProperty]
    private string _customAuthor = string.Empty;

    /// <summary>Release-date override, free text since the source .vpx's own year metadata is often missing or wrong.</summary>
    [ObservableProperty]
    private string _customDate = string.Empty;

    /// <summary>Hides this table from the library entirely - see NudgeSettings.TableCustomization.IsHidden.</summary>
    [ObservableProperty]
    private bool _isHidden;

    // ---------------------------------------------------------------- ROM availability

    /// <summary>True while IRomNameReader/IRomAvailabilityChecker are still working - both are per-table, on-demand reads (GameStg is too large to do for every table during a library scan; see IRomNameReader's own remarks), started once when this page opens.</summary>
    [ObservableProperty]
    private bool _isCheckingRom = true;

    /// <summary>Plain-English result of the ROM check, always set once IsCheckingRom is false.</summary>
    [ObservableProperty]
    private string _romStatusMessage = "Checking this table's ROM…";

    /// <summary>True once the check is done and everything looks fine (a ROM was found, or the table doesn't need one at all) - drives a green/good lamp, reusing the same Confidence.High brush the identification lamp uses elsewhere.</summary>
    [ObservableProperty]
    private bool _romLooksGood;

    /// <summary>True once the check is done and the ROM is confirmed missing - drives a warning lamp (Confidence.Low).</summary>
    [ObservableProperty]
    private bool _romLooksMissing;

    private async Task RunRomCheckAsync()
    {
        IsCheckingRom = true;
        try
        {
            Result<RomNameInfo> nameResult = await _romNameReader.ReadAsync(Tile.Table.Path).ConfigureAwait(true);

            if (nameResult.IsFailure || nameResult.Value.RomName is null)
            {
                RomStatusMessage = "This table doesn't reference a PinMAME ROM.";
                RomLooksGood = true;
                RomLooksMissing = false;
                return;
            }

            string romName = nameResult.Value.RomName;
            RomAvailability availability = await _romAvailabilityChecker.CheckAsync(romName).ConfigureAwait(true);

            switch (availability.Status)
            {
                case RomAvailabilityStatus.Found:
                    RomStatusMessage = $"ROM \"{romName}\" is present in your PinMAME ROM folder.";
                    RomLooksGood = true;
                    RomLooksMissing = false;
                    break;
                case RomAvailabilityStatus.Missing:
                    RomStatusMessage = $"ROM \"{romName}\" was not found in your PinMAME ROM folder - this table may not run without it.";
                    RomLooksGood = false;
                    RomLooksMissing = true;
                    break;
                default:
                    RomStatusMessage = $"Nudge couldn't determine your PinMAME ROM folder, so it can't confirm whether \"{romName}\" is present.";
                    RomLooksGood = false;
                    RomLooksMissing = false;
                    break;
            }
        }
        catch (Exception)
        {
            RomStatusMessage = "Nudge couldn't check this table's ROM right now.";
            RomLooksGood = false;
            RomLooksMissing = false;
        }
        finally
        {
            IsCheckingRom = false;
        }
    }

    // ---------------------------------------------------------------- Artwork browsing

    /// <summary>
    /// Every source worth offering in the picker dropdown - "vps-db" always, "Google Images" only once
    /// Library.GoogleCustomSearchApiKey/EngineId are both actually filled in under Settings. Offering
    /// it unconfigured would just search a source IArtworkBrowser itself reports "not configured" for
    /// every time, which reads as broken rather than as the deliberate gate it is.
    /// </summary>
    public IReadOnlyList<OptionItem<string>> ArtworkSourceOptions
    {
        get
        {
            bool googleConfigured = !string.IsNullOrWhiteSpace(Library.GoogleCustomSearchApiKey)
                                     && !string.IsNullOrWhiteSpace(Library.GoogleCustomSearchEngineId);

            return _artworkBrowser.AvailableSourceNames
                .Where(name => googleConfigured || !string.Equals(name, "Google Images", StringComparison.OrdinalIgnoreCase))
                .Select(name => new OptionItem<string>(name, name))
                .ToList();
        }
    }

    [ObservableProperty]
    private OptionItem<string>? _selectedBrowseSource;

    [ObservableProperty]
    private bool _isBrowsingArtwork;

    /// <summary>True once a search has actually been run - so the gallery/empty-state area only appears after the user asks for it, not just because the page opened.</summary>
    [ObservableProperty]
    private bool _hasBrowsedArtwork;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBrowseStatusMessage))]
    private string _browseStatusMessage = string.Empty;

    public bool HasBrowseStatusMessage => !string.IsNullOrWhiteSpace(BrowseStatusMessage);

    /// <summary>Lightweight candidates from the last search - not downloaded or cached until one is actually picked (SelectArtworkCandidateCommand).</summary>
    public ObservableCollection<ArtworkCandidate> ArtworkCandidates { get; } = [];

    // ============================ Trailer search ============================

    /// <summary>Videos found for this table, for the user to pick between. Empty until a search runs.</summary>
    public ObservableCollection<TrailerCandidate> TrailerCandidates { get; } = [];

    [ObservableProperty]
    private bool _isSearchingTrailers;

    /// <summary>Set once a search has run, so "no trailer found" only ever appears after actually looking.</summary>
    [ObservableProperty]
    private bool _hasSearchedTrailers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrailerStatusMessage))]
    private string _trailerStatusMessage = string.Empty;

    public bool HasTrailerStatusMessage => !string.IsNullOrWhiteSpace(TrailerStatusMessage);

    /// <summary>The video currently chosen for this table, or null when none is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTrailer))]
    [NotifyPropertyChangedFor(nameof(SelectedTrailerThumbnailUrl))]
    private string? _trailerYouTubeId;

    public bool HasSelectedTrailer => !string.IsNullOrWhiteSpace(TrailerYouTubeId);

    /// <summary>Still for the chosen video, so the page shows what is actually set rather than just an id.</summary>
    public string? SelectedTrailerThumbnailUrl =>
        HasSelectedTrailer ? $"https://img.youtube.com/vi/{TrailerYouTubeId}/hqdefault.jpg" : null;

    [RelayCommand]
    private async Task SearchTrailersAsync()
    {
        IsSearchingTrailers = true;
        HasSearchedTrailers = true;
        TrailerStatusMessage = string.Empty;
        TrailerCandidates.Clear();

        try
        {
            IReadOnlyList<TrailerCandidate> found = await _tableTrailerProvider
                .FindTrailersAsync(Tile.Table)
                .ConfigureAwait(true);

            foreach (TrailerCandidate candidate in found)
            {
                TrailerCandidates.Add(candidate);
            }

            if (found.Count == 0)
            {
                // Stated plainly rather than left as an empty gallery: most tables genuinely have no
                // video, and an empty result is an answer, not a failure.
                TrailerStatusMessage = "No trailer found for this table. You can still point Nudge at your own video file above.";
            }
        }
        catch (Exception)
        {
            TrailerStatusMessage = "Nudge couldn't reach the video database right now.";
        }
        finally
        {
            IsSearchingTrailers = false;
        }
    }

    [RelayCommand]
    private async Task SelectTrailerAsync(TrailerCandidate? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        TrailerYouTubeId = candidate.VideoId;
        Tile.TrailerYouTubeId = candidate.VideoId;
        TrailerStatusMessage = "Saved. Hover this table to see it play.";

        await SaveTrailerSelectionAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearTrailerAsync()
    {
        TrailerYouTubeId = null;
        Tile.TrailerYouTubeId = null;
        TrailerStatusMessage = string.Empty;

        await SaveTrailerSelectionAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Written immediately rather than waiting for the page's Save button: picking a video from a
    /// gallery reads as a completed action, and the artwork picker beside it behaves the same way.
    /// </summary>
    private async Task SaveTrailerSelectionAsync()
    {
        await _settingsService.MutateAsync(settings =>
        {
            if (!settings.TableCustomizations.TryGetValue(Tile.Table.Path, out TableCustomization? customization))
            {
                customization = new TableCustomization();
                settings.TableCustomizations[Tile.Table.Path] = customization;
            }

            customization.TrailerYouTubeId = TrailerYouTubeId;
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task BrowseArtworkAsync()
    {
        string? sourceName = SelectedBrowseSource?.Value;
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return;
        }

        IsBrowsingArtwork = true;
        HasBrowsedArtwork = true;
        BrowseStatusMessage = string.Empty;
        ArtworkCandidates.Clear();

        try
        {
            Result<IReadOnlyList<ArtworkCandidate>> result = await _artworkBrowser
                .SearchAsync(Tile.Table, sourceName)
                .ConfigureAwait(true);

            if (result.IsFailure)
            {
                BrowseStatusMessage = $"No images found from {sourceName}: {result.Error}";
                return;
            }

            foreach (ArtworkCandidate candidate in result.Value)
            {
                ArtworkCandidates.Add(candidate);
            }

            if (ArtworkCandidates.Count == 0)
            {
                BrowseStatusMessage = $"No images found from {sourceName} for this table.";
            }
        }
        catch (Exception)
        {
            BrowseStatusMessage = $"Nudge couldn't reach {sourceName} right now.";
        }
        finally
        {
            IsBrowsingArtwork = false;
        }
    }

    [RelayCommand]
    private async Task SelectArtworkCandidateAsync(ArtworkCandidate? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        IsBrowsingArtwork = true;
        try
        {
            Result<ArtworkImage> result = await _artworkBrowser
                .SelectAsync(Tile.Table, candidate)
                .ConfigureAwait(true);

            if (result.IsFailure)
            {
                BrowseStatusMessage = "Nudge couldn't download that image.";
                return;
            }

            // A hand-picked candidate wins the same way a manually-chosen file does - shown
            // immediately from the bytes IArtworkBrowser.SelectAsync already downloaded, resized and
            // cached, and pinned so it keeps winning after a restart too.
            Tile.SetArtworkFromBytes(result.Value.Data);
            CustomImagePath = null;

            await _settingsService.MutateAsync(settings =>
            {
                settings.TableArtworkSourceOverrides[Tile.Table.Path] = candidate.SourceName;

                // A leftover manual CustomImagePath always wins over IArtworkProvider (ApplyTables'
                // own rule) - without clearing it here too, this pick would look right immediately
                // but silently lose to the old manual image again on the next restart or rescan.
                if (settings.TableCustomizations.TryGetValue(Tile.Table.Path, out TableCustomization? existing))
                {
                    existing.CustomImagePath = null;
                }
            }).ConfigureAwait(true);

            ArtworkCandidates.Clear();
            HasBrowsedArtwork = false;
            BrowseStatusMessage = $"Using the image you picked from {candidate.SourceName}.";
        }
        finally
        {
            IsBrowsingArtwork = false;
        }
    }

    private async Task LoadAsync()
    {
        NudgeSettings settings = await _settingsService.LoadAsync().ConfigureAwait(true);
        if (settings.TableCustomizations.TryGetValue(Tile.Table.Path, out TableCustomization? existing))
        {
            Description = existing.Description;
            HowToPlay = existing.HowToPlay;
            CustomImagePath = existing.CustomImagePath;
            VideoPath = existing.VideoPath;
            TrailerYouTubeId = existing.TrailerYouTubeId;
            CustomTitle = existing.CustomTitle ?? string.Empty;
            CustomAuthor = existing.CustomAuthor ?? string.Empty;
            CustomDate = existing.CustomDate ?? string.Empty;
            IsHidden = existing.IsHidden;
        }
    }

    [RelayCommand]
    private void PickImage()
    {
        string? path = _filePickerService.PickImageFile($"Choose an image for {Tile.DisplayTitle}");
        if (path is not null)
        {
            // Remember Nudge's own copy rather than wherever the user picked from, so the cover
            // survives the original being renamed, moved or deleted later - see CustomArtworkStore.
            CustomImagePath = _customArtworkStore.Import(path, Tile.Table.Path);
        }
    }

    [RelayCommand]
    private void ClearImage() => CustomImagePath = null;

    [RelayCommand]
    private void PickVideo()
    {
        string? path = _filePickerService.PickVideoFile($"Choose a video for {Tile.DisplayTitle}");
        if (path is not null)
        {
            VideoPath = path;
        }
    }

    [RelayCommand]
    private void ClearVideo() => VideoPath = null;

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsSaving = true;
        try
        {
            bool isBlank = string.IsNullOrWhiteSpace(Description)
                           && string.IsNullOrWhiteSpace(HowToPlay)
                           && string.IsNullOrWhiteSpace(CustomImagePath)
                           && string.IsNullOrWhiteSpace(VideoPath)
                           && string.IsNullOrWhiteSpace(CustomTitle)
                           && string.IsNullOrWhiteSpace(CustomAuthor)
                           && string.IsNullOrWhiteSpace(CustomDate)
                           && !IsHidden;

            // Removes the entry entirely once every field is cleared, rather than leaving a stale
            // empty record behind - keeps the settings file from accumulating dead rows for every
            // table someone ever opened this page for and decided not to customize.
            TableCustomization? saved = null;

            // MutateAsync, not a separate LoadAsync+SaveAsync - a plain load-then-save here could
            // silently lose this exact save if it raced another settings write in flight at the same
            // moment (LibraryViewModel.SavePreferencesAsync fires from a dozen unrelated toggles), which
            // is exactly the "sometimes it wouldn't save fully, had to go back in and save again" bug
            // this was replaced to fix. See ISettingsService.MutateAsync's own remarks.
            await _settingsService.MutateAsync(settings =>
            {
                if (isBlank)
                {
                    settings.TableCustomizations.Remove(Tile.Table.Path);
                }
                else
                {
                    saved = new TableCustomization
                    {
                        Description = Description,
                        HowToPlay = HowToPlay,
                        CustomImagePath = CustomImagePath,
                        VideoPath = VideoPath,
                        TrailerYouTubeId = TrailerYouTubeId,
                        CustomTitle = string.IsNullOrWhiteSpace(CustomTitle) ? null : CustomTitle,
                        CustomAuthor = string.IsNullOrWhiteSpace(CustomAuthor) ? null : CustomAuthor,
                        CustomDate = string.IsNullOrWhiteSpace(CustomDate) ? null : CustomDate,
                        IsHidden = IsHidden
                    };
                    settings.TableCustomizations[Tile.Table.Path] = saved;
                }
            }).ConfigureAwait(true);

            // Reflects every choice on the live tile immediately, rather than waiting for a rescan -
            // a user-supplied override always wins over the scanned/provider-found value.
            if (!string.IsNullOrWhiteSpace(CustomImagePath))
            {
                Tile.SetCustomArtwork(CustomImagePath);
            }

            Tile.VideoPath = VideoPath;
            Tile.CustomTitle = string.IsNullOrWhiteSpace(CustomTitle) ? null : CustomTitle;
            Tile.CustomAuthor = string.IsNullOrWhiteSpace(CustomAuthor) ? null : CustomAuthor;
            Tile.CustomDate = string.IsNullOrWhiteSpace(CustomDate) ? null : CustomDate;
            Tile.IsHidden = IsHidden;

            // Keeps LibraryViewModel's own _tableCustomizations snapshot (and, in turn, Settings'
            // hidden-tables list) in step with what was just written to disk - without this, hiding a
            // table here wouldn't show up under Settings until the next full rescan reloaded
            // everything from the database.
            Library.RefreshTableCustomization(Tile, saved);
        }
        finally
        {
            IsSaving = false;
        }

        Back();
    }

    [RelayCommand]
    private void Back() => Library.EditingTableViewModel = null;
}
