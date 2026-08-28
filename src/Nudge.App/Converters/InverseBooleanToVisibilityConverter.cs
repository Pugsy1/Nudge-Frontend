using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nudge.App.Converters;

/// <summary>
/// The opposite of the stock <see cref="System.Windows.Controls.BooleanToVisibilityConverter"/>:
/// true collapses, false shows. Used for "empty state" panels that should appear only when a
/// collection is empty.
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}
