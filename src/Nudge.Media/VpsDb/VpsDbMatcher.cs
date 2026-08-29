using System.Text;
using Nudge.Core.Models;

namespace Nudge.Media.VpsDb;

/// <summary>
/// Matches a scanned table against vps-db's entries by title, disambiguating with manufacturer and
/// year when more than one entry shares a title. Pure text comparison - no I/O, no network - kept
/// separate from <see cref="IVpsDbIndex"/> so the matching rules are testable on their own.
/// </summary>
/// <remarks>
/// Titles are compared as **token sets**, not as normalised whole strings - measured against the
/// maintainer's real 61-table collection, plain exact-normalised matching only found 37 (61%).
/// Splitting into significant words (dropping stopwords, and splitting camelCase runs like
/// "BatmanDarkKnight" the same way a spaced title would) and allowing one side's word set to be
/// wholly contained in the other's recovered most of the rest, without special-casing any one
/// naming pattern:
/// <list type="bullet">
/// <item>"VR ROOM Attack from Mars" vs vps-db's "Attack from Mars" - a decorative prefix some VR
/// conversion authors add, not part of the real game title.</item>
/// <item>"Attack from Mars LE" / "Game of Thrones LE" / "X-Men LE" vs a base entry with no
/// "LE" - an edition suffix.</item>
/// <item>"BatmanDarkKnight" (camelCase, no separators at all) vs "Batman: The Dark Knight" - the
/// camelCase split plus dropping the stopword "The" makes these the same token set exactly.</item>
/// </list>
/// A single-word title is still only ever an exact set match, never treated as "contained in"
/// something longer - see <see cref="TokensMatch"/> - specifically so a short/generic title (e.g. a
/// table simply called "Mars") can never subset-match into an unrelated longer one ("Attack from
/// Mars"). This is still not a typo-tolerant fuzzy matcher: it will not correct a misspelling, and a
/// title that shares no token-set relationship with any entry is reported as no match, never a
/// best-guess nearest neighbour. See docs/RESEARCH-NOTES.md for the measured before/after.
/// </remarks>
public static class VpsDbMatcher
{
    private static readonly HashSet<string> Stopwords =
        new(StringComparer.OrdinalIgnoreCase) { "the", "a", "an", "of", "and", "in" };

    public static VpsDbEntry? FindMatch(VpxTableFile table, IReadOnlyList<VpsDbEntry> entries)
    {
        HashSet<string> tableTokens = Tokenize(table.DisplayTitle);
        if (tableTokens.Count == 0)
        {
            return null;
        }

        List<VpsDbEntry> titleMatches = entries
            .Where(e => TokensMatch(tableTokens, Tokenize(e.Name)))
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
            HashSet<string> manufacturerTokens = Tokenize(table.DisplayManufacturer);
            List<VpsDbEntry> manufacturerMatches = titleMatches
                .Where(e => e.Manufacturer is not null && Tokenize(e.Manufacturer).SetEquals(manufacturerTokens))
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

    /// <summary>Two token sets match when they are identical, or when the smaller is wholly contained
    /// in the larger - but only when the smaller side has at least two significant words, so a short
    /// or generic title is never treated as "contained in" an unrelated longer one.</summary>
    private static bool TokensMatch(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return false;
        }

        if (a.SetEquals(b))
        {
            return true;
        }

        HashSet<string> smaller = a.Count <= b.Count ? a : b;
        HashSet<string> larger = a.Count <= b.Count ? b : a;

        return smaller.Count >= 2 && smaller.IsSubsetOf(larger);
    }

    private static HashSet<string> Tokenize(string value)
    {
        // Splits at camelCase boundaries (an upper-case letter following a lower-case one) and at
        // letter/digit boundaries, so a concatenated title tokenizes the same way a normally spaced
        // one would: "BatmanDarkKnight" -> "Batman Dark Knight", and - the case that regressed a
        // real match on first pass, caught by re-measuring against real tables rather than assumed
        // fixed - "BlackKnight2000" -> "Black Knight 2000", not "Black Knight2000" (which shares no
        // token with vps-db's separately-tokenized "knight" and "2000"). Every other non-alphanumeric
        // run is treated as a separator too.
        var withBoundaries = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (i > 0)
            {
                char previous = value[i - 1];
                bool camelCaseBoundary = char.IsUpper(c) && char.IsLower(previous);
                bool letterDigitBoundary = char.IsDigit(c) != char.IsDigit(previous)
                                           && char.IsLetterOrDigit(c) && char.IsLetterOrDigit(previous);

                if (camelCaseBoundary || letterDigitBoundary)
                {
                    withBoundaries.Append(' ');
                }
            }

            withBoundaries.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return withBoundaries
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => !Stopwords.Contains(w))
            .ToHashSet();
    }
}
