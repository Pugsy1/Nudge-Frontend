using System.Windows;
using System.Windows.Controls;

namespace Nudge.App.Controls;

/// <summary>
/// Marks a slider as being actively adjusted by a controller.
///
/// A slider is the one header control that cannot simply be "pressed": it holds a value rather than
/// performing an action, so A enters an explicit adjust mode where left/right change the value and A
/// (or B) leaves again. That mode needs to be visible - otherwise passing over the slider and
/// actually moving it look identical, and the user has no way to tell which one they are in.
///
/// An attached property rather than a view-model flag because the state belongs to one particular
/// slider, and the template needs to read it as a plain trigger.
/// </summary>
public static class SliderBehavior
{
    public static readonly DependencyProperty IsAdjustingProperty =
        DependencyProperty.RegisterAttached(
            "IsAdjusting",
            typeof(bool),
            typeof(SliderBehavior),
            new PropertyMetadata(false));

    public static bool GetIsAdjusting(Slider slider) => (bool)slider.GetValue(IsAdjustingProperty);

    public static void SetIsAdjusting(Slider slider, bool value) => slider.SetValue(IsAdjustingProperty, value);
}
