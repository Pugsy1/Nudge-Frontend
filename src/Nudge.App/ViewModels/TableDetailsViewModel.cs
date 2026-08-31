using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nudge.Core.Abstractions;
using Nudge.Core.Models;

namespace Nudge.App.ViewModels;

/// <summary>
/// A read-and-watch page for one table: its artwork, everything the .vpx and vps-db know about it,
/// and its video.
///
/// Deliberately separate from <see cref="TableCustomizationViewModel"/>, which is the edit-and-
/// configure page. Most of what is shown here was already being scanned and stored - author,
/// version, release date, blurb, description, rules all come out of the table file's own OLE
/// TableInfo - and simply had nowhere to be displayed.
/// </summary>
public sealed partial class TableDetailsViewModel : ObservableObject
{
    private readonly ITableTrailerProvider _trailerProvider;
    private readonly LibraryViewModel _library;

    /// <summary>The owning library, so the details view can reach shared services such as the controller reader.</summary>
    public LibraryViewModel Library => _library;

    public TableDetailsViewModel(
        TableTileViewModel tile,
        LibraryViewModel library,
        ITableTrailerProvider trailerProvider)
    {
        Tile = tile;
        _library = library;
        _trailerProvider = trailerProvider;

        BuildFacts();
        _ = LoadTrailerAsync();
    }

    public TableTileViewModel Tile { get; }

    public string Title => Tile.DisplayTitle;

    public string Subtitle => Tile.Subtitle;

    // ---------------------------------------------------------------- Written descriptions

    /// <summary>
    /// The table author's own blurb - a one-or-two line pitch, where present. Kept separate from
    /// the long description because it reads as a standfirst rather than body copy.
    /// </summary>
    public string? Blurb => Clean(Tile.Table.TableInfo.TableBlurb);

    /// <summary>
    /// The author's description, or the user's own if they wrote one on the customization page.
    /// The user's wins: they wrote it precisely because they wanted something other than what the
    /// file already said.
    /// </summary>
    public string? Description => Clean(_library.GetCustomDescription(Tile.Table.Path))
                                  ?? Clean(Tile.Table.TableInfo.TableDescription);

    public string? Rules => Clean(_library.GetCustomHowToPlay(Tile.Table.Path))
                            ?? Clean(Tile.Table.TableInfo.TableRules);

    public bool HasBlurb => Blurb is not null;

    public bool HasDescription => Description is not null;

    public bool HasRules => Rules is not null;

    /// <summary>Shown in place of the body copy when a table carries no written information at all, which is common for older or hand-made tables.</summary>
    public bool HasNoWrittenDetail => !HasBlurb && !HasDescription && !HasRules;

    // ---------------------------------------------------------------- Facts table

    /// <summary>Label/value pairs for the specifications list. Built once - none of it changes while the page is open.</summary>
    public ObservableCollection<TableFact> Facts { get; } = [];

    private void BuildFacts()
    {
        TableInfoMetadata info = Tile.Table.TableInfo;

        Add("Manufacturer", Tile.Table.DisplayManufacturer);
        Add("Year", Tile.Table.DisplayYear?.ToString());
        Add("Table author", Clean(info.AuthorName));
        Add("Version", Clean(info.TableVersion));
        Add("Released", Clean(info.ReleaseDate));
        Add("Author website", Clean(info.AuthorWebSite));
        Add("Identification", Tile.ConfidenceLabel);
        Add("File", Tile.Table.FileName);
        Add("Size", FormatSize(Tile.Table.FileSizeBytes));

        void Add(string label, string? value)
        {
            // Blank rows are dropped rather than shown empty: a specifications list half full of
            // "-" reads as missing data, when in reality most tables simply never filled these in.
            if (!string.IsNullOrWhiteSpace(value))
            {
                Facts.Add(new TableFact(label, value!));
            }
        }
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024L * 1024 * 1024
            ? $"{bytes / (1024.0 * 1024 * 1024):0.0} GB"
            : $"{bytes / (1024.0 * 1024):0} MB";

    // ---------------------------------------------------------------- Video

    [ObservableProperty]
    private bool _isLoadingTrailer = true;

    /// <summary>The YouTube id to play here, or null once the lookup finds nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrailer))]
    private string? _trailerVideoId;

    public bool HasTrailer => !string.IsNullOrWhiteSpace(TrailerVideoId);

    /// <summary>True once the lookup has finished and come back empty - drives the "no video" message rather than a permanent spinner.</summary>
    public bool HasNoTrailer => !IsLoadingTrailer && !HasTrailer;

    partial void OnIsLoadingTrailerChanged(bool value) => OnPropertyChanged(nameof(HasNoTrailer));

    partial void OnTrailerVideoIdChanged(string? value) => OnPropertyChanged(nameof(HasNoTrailer));

    private async Task LoadTrailerAsync()
    {
        IsLoadingTrailer = true;
        try
        {
            // A video the user picked on the customization page wins over the automatic match, the
            // same way it does for the hover preview.
            TrailerVideoId = !string.IsNullOrWhiteSpace(Tile.TrailerYouTubeId)
                ? Tile.TrailerYouTubeId
                : await _trailerProvider.GetYouTubeVideoIdAsync(Tile.Table).ConfigureAwait(true);
        }
        catch (Exception)
        {
            TrailerVideoId = null;
        }
        finally
        {
            IsLoadingTrailer = false;
        }
    }

    [RelayCommand]
    private void Back() => _library.CloseTableDetails();

    [RelayCommand]
    private void Play() => _library.LaunchTableCommand.Execute(Tile);

    [RelayCommand]
    private void Customize()
    {
        _library.CloseTableDetails();
        _library.OpenTableCustomizationCommand.Execute(Tile);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One row of the specifications list.</summary>
public sealed record TableFact(string Label, string Value);
