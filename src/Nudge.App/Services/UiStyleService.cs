using System.Collections.ObjectModel;
using System.Windows;

namespace Nudge.App.Services;

/// <summary>
/// The overall material of the interface, independent of which colour palette is active. A style
/// decides shape and depth (corner radii, whether surfaces are shadow-defined or flat); an
/// <see cref="AppTheme"/> decides colour. Any style combines with any theme.
/// </summary>
public enum AppUiStyle
{
    /// <summary>Restrained soft-UI: modest rounding, a light shadow suggesting a raised surface.</summary>
    Pin,

    /// <summary>
    /// Full-strength sculpted neumorphism: big squircles, deeply extruded controls with a hard
    /// shadow down-right and a light one up-left, genuinely sunken slots, heavier type.
    /// </summary>
    Relief
}

/// <summary>Switches the application between the available interface styles.</summary>
public interface IUiStyleService
{
    AppUiStyle Current { get; }

    void Apply(AppUiStyle style);

    /// <summary>Parses a stored style name, falling back to Pin for anything unrecognised.</summary>
    AppUiStyle Parse(string? styleName);
}

/// <summary>
/// Swaps the style-overlay dictionary App.xaml merges after Layout.xaml and Effects.xaml.
///
/// The overlay works by key shadowing rather than by rewriting anything: WPF resolves a duplicate
/// key to whichever merged dictionary was added last, and every control template refers to these
/// radii and effects through DynamicResource, so redefining a key in the overlay re-skins the entire
/// app without a single control template knowing styles exist. Style.Pin.xaml is deliberately empty
/// - Pin *is* the base values in Layout/Effects, so its overlay has nothing to shadow.
///
/// Deliberately mirrors <see cref="ThemeService"/>, including swapping the top-level
/// MergedDictionaries entry by index rather than nesting a wrapper dictionary: see that class's own
/// remarks for why the nested approach fails to repaint already-rendered elements.
/// </summary>
public sealed class UiStyleService : IUiStyleService
{
    private static readonly IReadOnlyDictionary<AppUiStyle, string> StyleFileNames = new Dictionary<AppUiStyle, string>
    {
        [AppUiStyle.Pin] = "Style.Pin.xaml",
        [AppUiStyle.Relief] = "Style.Relief.xaml"
    };

    public AppUiStyle Current { get; private set; } = AppUiStyle.Pin;

    public AppUiStyle Parse(string? styleName) =>
        Enum.TryParse(styleName, ignoreCase: true, out AppUiStyle parsed) && StyleFileNames.ContainsKey(parsed)
            ? parsed
            : AppUiStyle.Pin;

    public void Apply(AppUiStyle style)
    {
        Application? application = Application.Current;
        if (application is null)
        {
            return;
        }

        Collection<ResourceDictionary> merged = application.Resources.MergedDictionaries;

        int overlayIndex = FindOverlayIndex(merged);
        var overlay = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{StyleFileNames[style]}")
        };

        if (overlayIndex >= 0)
        {
            // By index, not Remove+Add: the overlay has to stay after Layout/Effects (whose keys it
            // shadows) and before Controls.xaml. Re-adding at the end would still resolve correctly
            // today, but it silently breaks the ordering guarantee the whole mechanism rests on.
            merged[overlayIndex] = overlay;
        }
        else
        {
            merged.Add(overlay);
        }

        Current = style;
    }

    private static int FindOverlayIndex(Collection<ResourceDictionary> merged)
    {
        for (int i = 0; i < merged.Count; i++)
        {
            Uri? source = merged[i].Source;
            if (source is null)
            {
                continue;
            }

            foreach (string fileName in StyleFileNames.Values)
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
