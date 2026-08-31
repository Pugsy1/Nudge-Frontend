using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nudge.App.Converters;

/// <summary>
/// Shows an element only when its bound string has content, so an empty status or hint line takes
/// up no space at all rather than leaving a blank gap where text sometimes appears.
/// </summary>
public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Dims a controller row's press indicator instead of hiding it. Kept visible at low opacity so the
/// column reads as a consistent row of lamps that light up, rather than dots appearing and
/// disappearing and shifting the eye around as buttons are pressed.
/// </summary>
public sealed class BoolToPressOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.18;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
