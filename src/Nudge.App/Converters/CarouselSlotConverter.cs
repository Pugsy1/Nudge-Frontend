using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Nudge.App.Converters;

/// <summary>
/// Positions one tile in the "ring" carousel layout (LibraryView.xaml's CarouselTileTemplate) as a
/// point on a shallow arc, based on how far its absolute position in the library sits from the
/// ring's current (possibly fractional) scroll position - the carousel's visual math lives here
/// rather than in LibraryViewModel, since it is purely a presentation concern: the view model only
/// tracks scroll position and which tiles are currently realized, not how far apart they are drawn,
/// how strongly they fade, or that the centred one physically lifts above the rest of the arc.
///
/// Bound as a MultiBinding from three values:
///  1. The container's own ItemsControl.AlternationIndex - its ordinal within the realized window.
///  2. LibraryViewModel.CarouselWindowStart - the absolute index of the first tile in that window,
///     added to (1) to recover this tile's real position in the full (filtered/sorted) library.
///  3. LibraryViewModel.CarouselPosition - the ring's current scroll position, a double rather than
///     an int specifically so mouse wheel input can scrub smoothly through fractional positions
///     instead of only ever jumping a whole table at a time.
///
/// ConverterParameter selects which visual property this particular Setter produces ("Transform",
/// "Opacity", "ZIndex", or "Glow" - the last read directly inside CarouselTileTemplate's own
/// DataTemplate rather than the ItemContainerStyle the other three come from, since it targets an
/// element inside the tile's content, not the container itself), so one converter instance drives
/// all of them off the same angle.
/// </summary>
public sealed class CarouselSlotConverter : IMultiValueConverter
{
    // Angle step and window radius (LibraryViewModel.CarouselWindowRadius) together decide how much
    // of a full circle the visible tiles span - wider steps with fewer tiles reads as a proper round
    // ring with breathing room between tiles; narrower steps with more tiles is what previously read
    // as "crammed together" rather than circular.
    private const double AngleStepDegrees = 17;

    // Scaled to the shared TableCard's grid-matching size (Size.Tile.Width/Height) - every arc
    // dimension below tracks that size directly, so a later change to the card's own size doesn't
    // silently throw the ring's spacing out of proportion again.
    //
    // Large enough that adjacent tiles' pixel centres land roughly a card-width apart (at
    // AngleStepDegrees=17, RadiusX*sin(17 degrees) is about 228px against a 208px-wide card) - an
    // earlier, much smaller RadiusX put that same first neighbour barely 123px away while it was
    // still nearly fully opaque (cos(17 degrees) alone only fades it to ~96%), so it heavily
    // overlapped the centred tile instead of fanning out beside it - what actually read as "too
    // stacked", not the opacity curve itself.
    private const double RadiusX = 780;

    // Scaled up with RadiusX, in the same proportion, to keep the two close in magnitude - a shallow
    // ArcHeight next to a wide RadiusX is what made the arc read as a flat row with a slight sag
    // rather than an actual curving ring.
    private const double ArcHeight = 500;

    private const double MinScale = 0.35;
    private const double MinOpacity = 0.18;

    /// <summary>How far the centred tile lifts above the rest of the arc, in pixels at its peak - reduced from an earlier 90 both for headroom under the header and for a cleaner, less jarring lift.</summary>
    private const double RiseHeight = 55;

    /// <summary>Extra scale the centred tile gains on top of its normal full size at peak - the "rise" is a genuine pop, not just a vertical shift.</summary>
    private const double RiseScaleBoost = 0.16;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3
            || values[0] is not int alternationIndex || alternationIndex < 0
            || values[1] is not int windowStart
            || values[2] is not double position)
        {
            return Fallback(parameter);
        }

        double offset = (windowStart + alternationIndex) - position;
        double angle = offset * AngleStepDegrees * Math.PI / 180.0;

        // Clamped at 0 rather than left to go negative - depth also drives scale, and a raw cosine
        // would flip negative (turning a tile upside down for a frame) once |offset| pushes the
        // angle past 90 degrees, which happens routinely here since the window spans several slots
        // either side of centre.
        double depth = Math.Max(0, Math.Cos(angle));

        // A sharply peaked falloff (depth raised to a high power) rather than depth itself - this
        // stays near enough to zero for every tile except the one genuinely centred (or very close to
        // it mid-scroll) that only that one visibly rises, rather than the whole arc gaining a gentle
        // uniform lift. Continuous in offset, so it grows in and fades out smoothly through a scroll
        // or settle animation instead of popping in/out at some hard-coded threshold.
        double riseFactor = Math.Pow(depth, 8);
        double rise = riseFactor * RiseHeight;
        double scale = (MinScale + ((1 - MinScale) * depth)) * (1 + (RiseScaleBoost * riseFactor));

        return (parameter as string) switch
        {
            "Transform" => new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(scale, scale),
                    new TranslateTransform(RadiusX * Math.Sin(angle), (ArcHeight * (1 - depth)) - rise)
                }
            },
            "Opacity" => Math.Clamp(MinOpacity + ((1 - MinOpacity) * depth), 0, 1),
            "ZIndex" => (int)Math.Round(depth * 100),
            // A soft glow sitting behind each card (CarouselTileTemplate's Glow ellipse) - the same
            // sharply-peaked riseFactor already used for the rise/scale boost, so the glow only
            // reads as visible on the tile that's genuinely centred (or very close to it mid-scroll),
            // growing and fading smoothly along with the rise itself rather than as a separate effect.
            "Glow" => riseFactor,
            _ => Binding.DoNothing
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static object Fallback(object? parameter) => (parameter as string) switch
    {
        "Transform" => new TranslateTransform(),
        "Opacity" => 1.0,
        "ZIndex" => 0,
        "Glow" => 0.0,
        _ => Binding.DoNothing
    };
}
