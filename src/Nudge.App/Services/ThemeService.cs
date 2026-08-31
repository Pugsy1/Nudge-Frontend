using System.Collections.ObjectModel;
using System.Windows;

namespace Nudge.App.Services;

/// <summary>
/// Dark/Light are the two base materials (graphite vs porcelain); Jade/Sapphire/Crimson are the
/// same dark material with a different accent colour, following the pattern <c>Colors.Dark.xaml</c>
/// already established (Focus tied to Accent, everything else neutral grey), plus a faint tint of
/// that colour worked into the ambient background glow and surface bevel so the theme reads as more
/// than "a couple of icons changed colour". Chrome instead makes the accent a genuine polished-metal
/// gradient (bright/dark/bright reflection bands) rather than a hue, echoing the app's own disc
/// logo - see each palette file for its exact values.
/// </summary>
public enum AppTheme
{
    Dark,
    Light,
    Jade,
    JadeLight,
    Sapphire,
    SapphireLight,
    Crimson,
    CrimsonLight,
    Chrome,
    ChromeLight,
    Hulk,
    HulkLight,
    Oled,
    Amethyst,
    AmethystLight,
    Rose,
    RoseLight,
    Teal,
    TealLight,
    Coral,
    CoralLight,
    Indigo,
    IndigoLight,
    Lime,
    LimeLight,
    Magenta,
    MagentaLight,
    Slate,
    SlateLight,
    Copper,
    CopperLight,
    OledRed,
    OledBlue,
    OledGreen,
    OledPurple,

    // Calm, deliberately low-energy palettes - desaturated accents that recede behind the artwork.
    Sage,
    Dune,

    // Dual-tone: two accent hues at once rather than one (see any of these files' own header for
    // how the pair is carried through Accent.Surface, the bevel and the status lamps).
    Watermelon,
    BlueRaspberry,
    Citrus,
    Cosmic
}

/// <summary>Switches the application between the available palettes.</summary>
public interface IThemeService
{
    AppTheme Current { get; }

    void Apply(AppTheme theme);

    /// <summary>Parses a stored theme name, falling back to Dark for anything unrecognised.</summary>
    AppTheme Parse(string? themeName);
}

/// <summary>
/// Swaps the palette dictionary that App.xaml merges directly into Application.Resources.
///
/// This deliberately touches the top-level MergedDictionaries collection, not a dictionary nested
/// inside another one. An earlier version routed the swap through an extra wrapper dictionary
/// (Colors.xaml, itself merging Colors.Dark.xaml); WPF's DynamicResource invalidation did not
/// reliably reach already-rendered elements when that inner, two-levels-deep dictionary changed -
/// a fresh resource lookup saw the new colour immediately, but nothing already on screen repainted.
/// Swapping the entry Application.Resources.MergedDictionaries holds directly does not have that
/// problem, because every view resolves its colours with DynamicResource against that top level.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private static readonly IReadOnlyDictionary<AppTheme, string> PaletteFileNames = new Dictionary<AppTheme, string>
    {
        [AppTheme.Dark] = "Colors.Dark.xaml",
        [AppTheme.Light] = "Colors.Light.xaml",
        [AppTheme.Jade] = "Colors.Jade.xaml",
        [AppTheme.JadeLight] = "Colors.JadeLight.xaml",
        [AppTheme.Sapphire] = "Colors.Sapphire.xaml",
        [AppTheme.SapphireLight] = "Colors.SapphireLight.xaml",
        [AppTheme.Crimson] = "Colors.Crimson.xaml",
        [AppTheme.CrimsonLight] = "Colors.CrimsonLight.xaml",
        [AppTheme.Chrome] = "Colors.Chrome.xaml",
        [AppTheme.ChromeLight] = "Colors.ChromeLight.xaml",
        [AppTheme.Hulk] = "Colors.Hulk.xaml",
        [AppTheme.HulkLight] = "Colors.HulkLight.xaml",
        [AppTheme.Oled] = "Colors.Oled.xaml",
        [AppTheme.Amethyst] = "Colors.Amethyst.xaml",
        [AppTheme.AmethystLight] = "Colors.AmethystLight.xaml",
        [AppTheme.Rose] = "Colors.Rose.xaml",
        [AppTheme.RoseLight] = "Colors.RoseLight.xaml",
        [AppTheme.Teal] = "Colors.Teal.xaml",
        [AppTheme.TealLight] = "Colors.TealLight.xaml",
        [AppTheme.Coral] = "Colors.Coral.xaml",
        [AppTheme.CoralLight] = "Colors.CoralLight.xaml",
        [AppTheme.Indigo] = "Colors.Indigo.xaml",
        [AppTheme.IndigoLight] = "Colors.IndigoLight.xaml",
        [AppTheme.Lime] = "Colors.Lime.xaml",
        [AppTheme.LimeLight] = "Colors.LimeLight.xaml",
        [AppTheme.Magenta] = "Colors.Magenta.xaml",
        [AppTheme.MagentaLight] = "Colors.MagentaLight.xaml",
        [AppTheme.Slate] = "Colors.Slate.xaml",
        [AppTheme.SlateLight] = "Colors.SlateLight.xaml",
        [AppTheme.Copper] = "Colors.Copper.xaml",
        [AppTheme.CopperLight] = "Colors.CopperLight.xaml",
        [AppTheme.OledRed] = "Colors.OledRed.xaml",
        [AppTheme.OledBlue] = "Colors.OledBlue.xaml",
        [AppTheme.OledGreen] = "Colors.OledGreen.xaml",
        [AppTheme.OledPurple] = "Colors.OledPurple.xaml",
        [AppTheme.Sage] = "Colors.Sage.xaml",
        [AppTheme.Dune] = "Colors.Dune.xaml",
        [AppTheme.Watermelon] = "Colors.Watermelon.xaml",
        [AppTheme.BlueRaspberry] = "Colors.BlueRaspberry.xaml",
        [AppTheme.Citrus] = "Colors.Citrus.xaml",
        [AppTheme.Cosmic] = "Colors.Cosmic.xaml"
    };

    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public AppTheme Parse(string? themeName) =>
        Enum.TryParse(themeName, ignoreCase: true, out AppTheme parsed) && PaletteFileNames.ContainsKey(parsed)
            ? parsed
            : AppTheme.Dark;

    public void Apply(AppTheme theme)
    {
        Application? application = Application.Current;
        if (application is null)
        {
            return;
        }

        Collection<ResourceDictionary> merged = application.Resources.MergedDictionaries;

        int paletteIndex = FindPaletteIndex(merged);
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{PaletteFileNames[theme]}")
        };

        if (paletteIndex >= 0)
        {
            // Assigning by index, rather than Remove+Add, keeps the palette in the same position so
            // dictionaries merged after it (Controls.xaml, which relies on these keys existing) are
            // never left pointing at a moment where the palette is briefly absent.
            merged[paletteIndex] = palette;
        }
        else
        {
            // The application resources are not shaped the way this service expects - there is no
            // existing palette entry to replace. Adding one is still better than doing nothing.
            merged.Add(palette);
        }

        Current = theme;
    }

    /// <summary>Finds whichever palette dictionary is currently merged in.</summary>
    private static int FindPaletteIndex(Collection<ResourceDictionary> merged)
    {
        for (int i = 0; i < merged.Count; i++)
        {
            Uri? source = merged[i].Source;
            if (source is null)
            {
                continue;
            }

            foreach (string fileName in PaletteFileNames.Values)
            {
                if (source.OriginalString.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }
}
