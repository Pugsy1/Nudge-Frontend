using System.Collections.ObjectModel;
using System.Windows;

namespace Nudge.App.Services;

public enum AppTheme
{
    Dark,
    Light
}

/// <summary>Switches the application between the light and dark palettes.</summary>
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
    private const string DarkPaletteUri = "pack://application:,,,/Themes/Colors.Dark.xaml";
    private const string LightPaletteUri = "pack://application:,,,/Themes/Colors.Light.xaml";

    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public AppTheme Parse(string? themeName) =>
        string.Equals(themeName, nameof(AppTheme.Light), StringComparison.OrdinalIgnoreCase)
            ? AppTheme.Light
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
            Source = new Uri(theme == AppTheme.Light ? LightPaletteUri : DarkPaletteUri)
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

    /// <summary>Finds whichever of the two palette dictionaries is currently merged in.</summary>
    private static int FindPaletteIndex(Collection<ResourceDictionary> merged)
    {
        for (int i = 0; i < merged.Count; i++)
        {
            Uri? source = merged[i].Source;
            if (source is not null
                && (source.OriginalString.EndsWith("Colors.Dark.xaml", StringComparison.OrdinalIgnoreCase)
                    || source.OriginalString.EndsWith("Colors.Light.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return -1;
    }
}
