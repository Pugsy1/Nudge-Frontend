using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Nudge.App.Converters;

/// <summary>
/// Shows an element when a bound enum value equals the enum value passed as
/// <c>ConverterParameter</c> (typically via <c>{x:Static}</c>), collapses it otherwise.
///
/// Used to switch between the setup screen's stages without a separate navigation framework -
/// Phase 1 has exactly one screen with three states, which does not warrant one.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && value.Equals(parameter)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException($"{nameof(EnumEqualsConverter)} only supports one-way binding.");
}
