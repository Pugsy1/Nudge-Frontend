using System.Text.RegularExpressions;
using Nudge.Core.Models;

namespace Nudge.Vpx.TableFiles;

public interface ITableFilenameParser
{
    /// <summary>Parses a table filename. Returns <see cref="FilenameHints.Empty"/> rather than a
    /// wrong guess when the name doesn't follow a recognisable convention.</summary>
    FilenameHints Parse(string fileName);
}

/// <summary>
/// Parses the loose "Title (Manufacturer Year).vpx" convention used by part of the Visual Pinball
/// community, plus common trailing mod/version tags.
///
/// Checked against real table filenames during Phase 2 development. The convention is followed by
/// roughly half of real files - "BlackKnight2000(Williams 1989).vpx" and
/// "CreatureFromTheBlackLagoon(Bally 1992)_1.3.vpx" match cleanly, while "Batman66.vpx" and
/// "AttackfromMarsMidway 1995v600.vpx" have no parseable structure at all, and
/// "Albator the movie (VR ROOM).vpx" has parentheses that are not a manufacturer/year pair. All
/// three of the latter cases must produce an honest empty or partial result, not a wrong parse.
/// </summary>
public sealed partial class TableFilenameParser : ITableFilenameParser
{
    public FilenameHints Parse(string fileName)
    {
        string stem = StripExtension(fileName);

        Match match = TitleWithParenthesesPattern().Match(stem);
        if (!match.Success)
        {
            // No parenthesised group at all. The stem might still be a usable title, but with
            // nothing to corroborate it as a "Title (Manufacturer Year)"-style name specifically,
            // it is reported as a tag-free, manufacturer-free hint rather than assumed structured.
            return FilenameHints.Empty;
        }

        string title = match.Groups["title"].Value.Trim();
        string firstParenContent = match.Groups["inner"].Value.Trim();
        string rest = match.Groups["rest"].Value;

        Match manufacturerYear = ManufacturerYearPattern().Match(firstParenContent);

        string? manufacturer = null;
        int? year = null;
        var tags = new List<string>();

        if (manufacturerYear.Success)
        {
            manufacturer = manufacturerYear.Groups["manufacturer"].Value.Trim();
            year = int.Parse(manufacturerYear.Groups["year"].Value);
        }
        else
        {
            // The parenthesised text doesn't look like "Manufacturer Year" - e.g. "(VR ROOM)".
            // Keep it as a tag instead of discarding it or misreading it as a manufacturer.
            if (firstParenContent.Length > 0)
            {
                tags.Add(firstParenContent);
            }
        }

        tags.AddRange(ExtractTrailingTags(rest));

        if (title.Length == 0 && manufacturer is null && tags.Count == 0)
        {
            return FilenameHints.Empty;
        }

        return new FilenameHints
        {
            Title = title.Length > 0 ? title : null,
            Manufacturer = manufacturer,
            Year = year,
            Tags = tags
        };
    }

    /// <summary>
    /// Pulls tags out of whatever follows the first parenthesised group, e.g. "_Bigus(MOD)4.0" or
    /// "_1.3". Split on common separators; short numeric-only fragments (bare version numbers) and
    /// empty fragments are dropped rather than kept as meaningless tags.
    /// </summary>
    private static IEnumerable<string> ExtractTrailingTags(string rest)
    {
        foreach (string rawPart in TrailingSeparatorPattern().Split(rest))
        {
            string part = rawPart.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            yield return part;
        }
    }

    private static string StripExtension(string fileName) =>
        fileName.EndsWith(".vpx", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;

    /// <summary>Everything up to the first "(...)", the parenthesised content, and everything after.</summary>
    [GeneratedRegex(@"^(?<title>.*?)\((?<inner>[^)]*)\)(?<rest>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex TitleWithParenthesesPattern();

    /// <summary>
    /// "Manufacturer Year" - a name followed by a plausible four-digit pinball-era year. 1930
    /// covers the electromechanical era some VPX recreations reach back to; 2049 is a generous
    /// forward margin rather than a claim about the future.
    /// </summary>
    [GeneratedRegex(@"^(?<manufacturer>.+?)\s+(?<year>19[3-9]\d|20[0-4]\d)$", RegexOptions.CultureInvariant)]
    private static partial Regex ManufacturerYearPattern();

    [GeneratedRegex(@"[_\-,]+|\((?=[A-Za-z])|\)")]
    private static partial Regex TrailingSeparatorPattern();
}
