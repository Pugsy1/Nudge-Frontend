using System.Text.RegularExpressions;

namespace Nudge.App.Services;

/// <summary>
/// Recognises the test fixtures, physics rigs and calibration benches that ship alongside real
/// tables in most VPX collections - "JP's VPX7 Elasticity_Test.vpx" and friends. They scan and
/// identify perfectly well; they just aren't tables anyone wants to see in a library, so Nudge
/// hides them by default rather than dropping them (see LibraryViewModel.ApplyTables - a hidden
/// table is still listed under Settings' hidden-tables section and can be brought back, whereas one
/// filtered out of the list entirely would be unreachable).
/// </summary>
public static partial class TestTableHeuristics
{
    /// <summary>
    /// Matched against the title and the filename with separators normalised to spaces.
    ///
    /// Every term is anchored with \b word boundaries rather than a plain substring search, because
    /// substring matching is actively dangerous here: "test" appears inside "Contest", "Protest" and
    /// "Greatest", and "demo" inside "Demolition Man" - a real, popular table that a naive
    /// Contains("demo") would silently hide from every user who owns it.
    /// </summary>
    [GeneratedRegex(
        @"\b(elasticity|calibration|calibrate|benchmark|bouncetest|physics\s*test|flipper\s*test|"
        + @"test\s*table|table\s*test|test\s*bench|testbed|test|tests|testing)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TestFixturePattern();

    /// <summary>
    /// Underscores, dots and dashes are treated as spaces first: real fixture names lean on them
    /// heavily ("Elasticity_Test", "physics-test", "VPX7.Test"), and without this the word
    /// boundaries those terms depend on never line up.
    /// </summary>
    [GeneratedRegex(@"[_\.\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorPattern();

    /// <summary>
    /// True when a table looks like a test fixture rather than something playable. Checks the
    /// display title and the filename independently, since a fixture often carries a generic title
    /// in its OLE metadata while only the filename gives it away (or the reverse).
    /// </summary>
    public static bool LooksLikeTestFixture(string? displayTitle, string? fileName) =>
        IsMatch(displayTitle) || IsMatch(fileName);

    private static bool IsMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalised = SeparatorPattern().Replace(value, " ");
        return TestFixturePattern().IsMatch(normalised);
    }
}
