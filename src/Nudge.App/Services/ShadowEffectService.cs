using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Nudge.App.Services;

/// <summary>Turns the theme's soft drop shadows on/off and scales their strength - the Settings page's shadow controls.</summary>
public interface IShadowEffectService
{
    void Apply(bool enabled, int intensityPercent);
}

/// <summary>
/// Rewrites every DropShadowEffect resource Themes/Effects.xaml defines, directly in
/// Application.Resources, the same top-level-swap approach <see cref="ThemeService"/> uses for
/// palettes (see the remarks there on why a nested dictionary swap does not reliably repaint
/// elements already on screen).
///
/// Only Opacity is scaled - BlurRadius and ShadowDepth stay exactly as Effects.xaml authored them.
/// Those two were deliberately sized down to fit the actual whitespace each control has to bleed a
/// shadow into (a Popup's own margin, a tile's grid gutter) without hitting that boundary and
/// hard-clipping instead of fading out. Letting a user's intensity slider scale them back up would
/// silently reintroduce exactly that bug, so intensity only ever dims or brightens the existing
/// shadow, never widens it.
///
/// The base Depth/Blur/Opacity values below are mirrored from Effects.xaml rather than read back
/// from the live resource at apply time, since re-scaling an already-scaled value would compound
/// drift every time the slider moves. Keep the two in sync if either changes.
///
/// Each replacement effect's Color is read once from the current palette's Color.Shadow/
/// Color.Highlight resource, as a plain value rather than a live {DynamicResource} binding - Effect
/// derives from Freezable, not FrameworkElement, so it has no SetResourceReference to reproduce that
/// markup extension from code. That means a theme switch needs to call Apply again to re-tint
/// (LibraryViewModel.OnSelectedThemeChanged does this), the same way it already re-applies the
/// palette itself.
/// </summary>
public sealed class ShadowEffectService : IShadowEffectService
{
    private sealed record ShadowSpec(double Direction, double ShadowDepth, double BlurRadius, double Opacity, bool UsesHighlightColor);

    private static readonly IReadOnlyDictionary<string, ShadowSpec> BaseEffects = new Dictionary<string, ShadowSpec>
    {
        ["Effect.Card.Shadow"] = new(270, 5, 16, 0.28, false),
        ["Effect.Button.Shadow"] = new(270, 2, 7, 0.22, false),
        ["Effect.Button.Shadow.Hover"] = new(270, 3, 11, 0.26, false),
        ["Effect.Button.Shadow.Pressed"] = new(270, 1, 3, 0.18, false),
        ["Effect.Tile.Shadow.Rest"] = new(315, 3, 8, 0.42, false),
        ["Effect.Tile.Shadow.Hover"] = new(315, 6, 14, 0.5, false),
        ["Effect.Tile.Shadow.Pressed"] = new(315, 1, 3, 0.4, false),
        ["Effect.Tile.Highlight.Rest"] = new(135, 3, 8, 0.32, true),
        ["Effect.Tile.Highlight.Hover"] = new(135, 6, 14, 0.4, true),
        ["Effect.Tile.Highlight.Pressed"] = new(135, 1, 3, 0.32, true),
        ["Effect.Inset.Shadow"] = new(270, 1, 5, 0.22, false),
        ["Effect.Flyout.Shadow"] = new(270, 6, 18, 0.32, false),
        ["Effect.Card.Shadow.Rest"] = new(315, 4, 14, 0.38, false),
        ["Effect.Card.Highlight.Rest"] = new(135, 4, 14, 0.3, true),
    };

    public void Apply(bool enabled, int intensityPercent)
    {
        Application? application = Application.Current;
        if (application is null)
        {
            return;
        }

        double multiplier = Math.Clamp(intensityPercent, 0, 175) / 100.0;
        Color shadowColor = ResolveColor(application, "Color.Shadow");
        Color highlightColor = ResolveColor(application, "Color.Highlight");

        foreach ((string key, ShadowSpec spec) in BaseEffects)
        {
            if (!enabled)
            {
                application.Resources[key] = null;
                continue;
            }

            application.Resources[key] = new DropShadowEffect
            {
                Direction = spec.Direction,
                ShadowDepth = spec.ShadowDepth,
                BlurRadius = spec.BlurRadius,
                Opacity = Math.Clamp(spec.Opacity * multiplier, 0, 1),
                RenderingBias = RenderingBias.Performance,
                Color = spec.UsesHighlightColor ? highlightColor : shadowColor
            };
        }
    }

    private static Color ResolveColor(Application application, string resourceKey) =>
        application.Resources[resourceKey] is Color color ? color : Colors.Black;
}
