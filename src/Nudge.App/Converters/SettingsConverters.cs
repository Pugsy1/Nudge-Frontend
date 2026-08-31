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
