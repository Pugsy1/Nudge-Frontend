using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Nudge.App.Controls;

/// <summary>
/// Eases a <see cref="ProgressBar"/> toward each new value instead of snapping to it.
///
/// A scan reports progress in discrete jumps - one report per file, and files that are already
/// unchanged are skipped so fast that the bar leaps several percent at a time, then sits still.
/// Bound straight to Value that reads as a stuttering bar rather than a loading one, however
/// nicely the track itself is drawn. Animating the gap closed turns the same underlying numbers
/// into continuous motion.
///
/// Use <see cref="SmoothValueProperty"/> in place of binding Value directly.
/// </summary>
public static class ProgressBarBehavior
{
    /// <summary>
    /// How long the bar takes to catch up to a newly reported value. Long enough to read as motion,
    /// short enough that the bar is never meaningfully behind what the scan has actually done.
    /// </summary>
    private static readonly Duration CatchUpDuration = new(TimeSpan.FromMilliseconds(280));

    public static readonly DependencyProperty SmoothValueProperty =
        DependencyProperty.RegisterAttached(
            "SmoothValue",
            typeof(double),
            typeof(ProgressBarBehavior),
            new PropertyMetadata(0.0, OnSmoothValueChanged));

    public static double GetSmoothValue(DependencyObject element) =>
        (double)element.GetValue(SmoothValueProperty);

    public static void SetSmoothValue(DependencyObject element, double value) =>
        element.SetValue(SmoothValueProperty, value);

    private static void OnSmoothValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ProgressBar bar || e.NewValue is not double target)
        {
            return;
        }

        // A reset back to zero (a new scan starting) is applied instantly. Animating it would play
        // the whole bar draining backwards, which looks like progress being undone rather than a
        // fresh start.
        if (target <= 0)
        {
            bar.BeginAnimation(RangeBase_ValueProperty, null);
            bar.Value = 0;
            return;
        }

        var animation = new DoubleAnimation(target, CatchUpDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },

            // Holds the final value rather than releasing the animation and snapping back to
            // whatever Value was last set locally.
            FillBehavior = FillBehavior.HoldEnd
        };

        bar.BeginAnimation(RangeBase_ValueProperty, animation);
    }

    /// <summary>ProgressBar.Value is declared on RangeBase, which is what an animation has to target.</summary>
    private static readonly DependencyProperty RangeBase_ValueProperty =
        System.Windows.Controls.Primitives.RangeBase.ValueProperty;
}
