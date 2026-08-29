using Nudge.Core.Models;

namespace Nudge.Media.VpsDb;

/// <summary>
/// Matches a scanned table against vps-db's entries by title, disambiguating with manufacturer and
/// year when more than one entry shares a title. Pure text comparison - no I/O, no network - kept
/// separate from <see cref="IVpsDbIndex"/> so the matching rules are testable on their own.
/// </summary>
/// <remarks>
/// This is a simple normalised-equality match (strip everything but letters/digits, lowercase), the
/// same level of rigour <c>Nudge.Vpx.TableFiles.VpxTableFileReader</c> uses for reconciling OLE
/// metadata against a filename - not a fuzzy/typo-tolerant matcher. A title that does not normalise
/// to an exact match against any vps-db entry is reported as no match, never a best-guess nearest
/// neighbour; see docs/RESEARCH-NOTES.md.
/// </remarks>
public static class VpsDbMatcher
{
    public static VpsDbEntry? FindMatch(VpxTableFile table, IReadOnlyList<VpsDbEntry> entries)
    {
        string normalisedTitle = Normalise(table.DisplayTitle);
        if (normalisedTitle.Length == 0)
        {
            return null;
        }

        List<VpsDbEntry> titleMatches = entries
            .Where(e => Normalise(e.Name) == normalisedTitle)
            .ToList();

        if (titleMatches.Count == 0)
        {
            return null;
        }

        if (titleMatches.Count == 1)
        {
            return titleMatches[0];
        }

        // More than one table shares this title (common for widely-modded originals). Narrow by
        // manufacturer first, then by year, rather than guessing - if narrowing still leaves more
        // than one candidate, the first is used, matching RomNameParser's "first wins, record why"
        // approach to an unavoidable ambiguity rather than refusing to answer at all.
        if (table.DisplayManufacturer is not null)
        {
            string normalisedManufacturer = Normalise(table.DisplayManufacturer);
            List<VpsDbEntry> manufacturerMatches = titleMatches
                .Where(e => e.Manufacturer is not null && Normalise(e.Manufacturer) == normalisedManufacturer)
                .ToList();

            if (manufacturerMatches.Count > 0)
            {
                titleMatches = manufacturerMatches;
            }
        }

        if (titleMatches.Count > 1 && table.DisplayYear is not null)
        {
            List<VpsDbEntry> yearMatches = titleMatches
                .Where(e => e.Year == table.DisplayYear)
                .ToList();

            if (yearMatches.Count > 0)
            {
                titleMatches = yearMatches;
            }
        }

        return titleMatches[0];
    }

    /// <summary>The best directly-fetchable image URL for an entry, or null when it has none. Table
    /// (playfield) screenshots are preferred over backglasses only because they are more consistently
    /// present in the real dataset - see docs/RESEARCH-NOTES.md.</summary>
    public static string? BestImageUrl(VpsDbEntry entry, out string sourceDescription)
    {
        string? tableImage = entry.TableFiles.Select(f => f.ImgUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        if (tableImage is not null)
        {
            sourceDescription = "Table image (vps-db)";
            return tableImage;
        }

        string? backglassImage = entry.B2SFiles.Select(f => f.ImgUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        if (backglassImage is not null)
        {
            sourceDescription = "Backglass (vps-db)";
            return backglassImage;
        }

        sourceDescription = string.Empty;
        return null;
    }

    private static string Normalise(string value) =>
        new string([.. value.Where(char.IsLetterOrDigit)]).ToLowerInvariant();
}
