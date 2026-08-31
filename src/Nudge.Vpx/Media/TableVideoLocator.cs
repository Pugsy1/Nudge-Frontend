using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;

namespace Nudge.Vpx.Media;

/// <summary>
/// Looks for a table's video by filename across the folder layouts the common VPX frontends use.
///
/// Verified against the conventions documented for PinUP Popper (<c>POPMedia/Visual Pinball
/// X/Table Videos</c>) and PinballX (<c>Media/Visual Pinball/Table Videos</c>), plus the plain
/// "video sits next to the .vpx" arrangement most standalone media packs ship.
///
/// Two passes. First an exact filename probe, which is cheap and unambiguous. Then a normalised
/// match against the folder's listing, because that is what actually finds anything in a real media
/// pack: those name their files after the table ("Medieval Madness (Williams 1997).mp4"), not after
/// whatever the user's own .vpx happens to be called ("MedievalMadness_VPW_1.0.2.vpx"). Only the
/// second pass enumerates a directory, and those listings are cached for the session.
/// </summary>
public sealed partial class TableVideoLocator : ITableVideoLocator
{
    /// <summary>
    /// Ordered by how likely each is to be the "real" playfield video. MP4 first because it is what
    /// modern packs ship; f4v is PinballX's historic default and still very common.
    /// </summary>
    private static readonly string[] Extensions = [".mp4", ".f4v", ".mkv", ".webm", ".mov", ".avi", ".wmv"];

    /// <summary>
    /// Media-folder layouts, relative to the installation root. Playfield/table videos only - the
    /// backglass and DMD folders deliberately aren't searched, since those are landscape clips of a
    /// completely different subject and would look wrong cropped into a portrait tile.
    /// </summary>
    private static readonly string[] RelativeMediaFolders =
    [
        @"POPMedia\Visual Pinball X\Table Videos",
        @"POPMedia\Visual Pinball\Table Videos",
        @"Media\Visual Pinball X\Table Videos",
        @"Media\Visual Pinball\Table Videos",
        @"Media\Table Videos",
        "Videos"
    ];

    private readonly IFileSystem _fileSystem;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<TableVideoLocator> _logger;

    public TableVideoLocator(IFileSystem fileSystem, IPathRedactor redactor, ILogger<TableVideoLocator> logger)
    {
        _fileSystem = fileSystem;
        _redactor = redactor;
        _logger = logger;
    }

    public string? Locate(string vpxFilePath, string? installationRootPath, string? displayTitle = null)
    {
        if (string.IsNullOrWhiteSpace(vpxFilePath))
        {
            return null;
        }

        try
        {
            string baseName = _fileSystem.Path.GetFileNameWithoutExtension(vpxFilePath);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return null;
            }

            string[] folders = CandidateFolders(vpxFilePath, installationRootPath).ToArray();

            // Pass 1 - exact filename. Cheapest, and an exactly-named file is the strongest possible
            // signal that it was put there for this table specifically.
            foreach (string folder in folders)
            {
                foreach (string extension in Extensions)
                {
                    string candidate = _fileSystem.Path.Combine(folder, baseName + extension);
                    if (_fileSystem.File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            // Pass 2 - normalised match, which is what actually finds anything in a real media pack.
            // Only reached when pass 1 failed, and each folder's listing is cached, so the directory
            // enumeration happens once per folder for the whole session rather than once per table.
            return FuzzyMatch(folders, baseName, displayTitle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A missing video is the ordinary case, so a failure to look for one is never worth
            // surfacing - the tile simply shows its artwork, exactly as it did before.
            _logger.LogDebug(ex, "Could not look for a video for {Path}.", _redactor.Redact(vpxFilePath));
            return null;
        }
    }

    /// <summary>
    /// Folder listings, keyed by folder. A media folder can hold thousands of files and every tile
    /// that finds nothing in pass 1 would otherwise re-enumerate the same folders on every hover.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<(string Normalised, string Path)>> _folderCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Matches on normalised names - case, spaces, punctuation and bracketed suffixes removed - so
    /// "Medieval Madness (Williams 1997).mp4" is found for a table titled "Medieval Madness".
    ///
    /// Requires one name to fully contain the other rather than scoring partial similarity, and
    /// requires at least 6 normalised characters to do it. Loose fuzzy matching here is worse than
    /// no match at all: silently playing the wrong table's video over a tile is confusing in a way
    /// that a tile simply showing its artwork is not, and short names ("ACDC", "Tron") collide with
    /// each other constantly.
    /// </summary>
    private string? FuzzyMatch(string[] folders, string baseName, string? displayTitle)
    {
        string[] wanted = new[] { Normalise(baseName), Normalise(displayTitle) }
            .Where(n => n.Length >= 6)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wanted.Length == 0)
        {
            return null;
        }

        foreach (string folder in folders)
        {
            foreach ((string normalised, string path) in ListVideos(folder))
            {
                foreach (string want in wanted)
                {
                    if (normalised.Contains(want, StringComparison.Ordinal)
                        || want.Contains(normalised, StringComparison.Ordinal))
                    {
                        return path;
                    }
                }
            }
        }

        return null;
    }

    private IReadOnlyList<(string Normalised, string Path)> ListVideos(string folder)
    {
        if (_folderCache.TryGetValue(folder, out IReadOnlyList<(string, string)>? cached))
        {
            return cached;
        }

        List<(string Normalised, string Path)> found = [];
        try
        {
            if (_fileSystem.Directory.Exists(folder))
            {
                foreach (string file in _fileSystem.Directory.EnumerateFiles(folder))
                {
                    if (Extensions.Contains(_fileSystem.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    {
                        found.Add((Normalise(_fileSystem.Path.GetFileNameWithoutExtension(file)), file));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not list videos in {Path}.", _redactor.Redact(folder));
        }

        _folderCache[folder] = found;
        return found;
    }

    /// <summary>
    /// Strips bracketed segments first, then reduces to lowercase letters and digits.
    ///
    /// Dropping "(Williams 1997)" / "[VPW]" before comparing is what makes the containment test
    /// work on real names: a title carrying an edition marker the pack's name lacks ("Attack from
    /// Mars LE" vs "Attack From Mars") only matches once the trailing manufacturer/year is out of
    /// the way - with it left in, the two differ in the MIDDLE and neither contains the other.
    ///
    /// The cost is losing year/manufacturer as a disambiguator, so two builds of the same table
    /// normalise identically and the first found wins. That is the right trade: they are the same
    /// table, so either video is a reasonable preview.
    /// </summary>
    private static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string withoutBrackets = BracketedSegmentPattern().Replace(value, " ");
        return new string(withoutBrackets.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    [GeneratedRegex(@"[\(\[\{][^\)\]\}]*[\)\]\}]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketedSegmentPattern();

    private IEnumerable<string> CandidateFolders(string vpxFilePath, string? installationRootPath)
    {
        // Next to the .vpx first: an explicitly paired file beats anything in a shared media folder,
        // because it was almost certainly put there deliberately for this exact table.
        string? tableFolder = _fileSystem.Path.GetDirectoryName(vpxFilePath);
        if (!string.IsNullOrWhiteSpace(tableFolder))
        {
            yield return tableFolder;

            // Subfolders of the tables directory that media packs commonly drop videos into.
            yield return _fileSystem.Path.Combine(tableFolder, "Videos");
            yield return _fileSystem.Path.Combine(tableFolder, "Media");
        }

        if (string.IsNullOrWhiteSpace(installationRootPath))
        {
            yield break;
        }

        foreach (string relative in RelativeMediaFolders)
        {
            yield return _fileSystem.Path.Combine(installationRootPath, relative);
        }

        // Frontends are frequently installed alongside Visual Pinball rather than inside it, so the
        // same layouts are worth trying one level up as well.
        string? parent = _fileSystem.Path.GetDirectoryName(installationRootPath.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(parent))
        {
            yield break;
        }

        foreach (string relative in RelativeMediaFolders)
        {
            yield return _fileSystem.Path.Combine(parent, relative);
        }
    }
}
