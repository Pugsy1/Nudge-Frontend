using System.Globalization;
using System.Windows.Data;

namespace Nudge.App.Converters;

/// <summary>
/// Turns a Slider's Value/Minimum/Maximum plus its track's rendered width into the pixel width of
/// the "filled" portion behind the thumb - WPF's Slider has no such element built in, so
/// Slider.Standard (Controls.xaml) draws it as a separate Border and this converter is what keeps
/// that Border's width in sync with the thumb as it's dragged, clicked, or moved by keyboard.
/// </summary>
public sealed class SliderFillWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [double value, double minimum, double maximum, double trackWidth]
            || maximum <= minimum)
        {
            return 0d;
        }

        double fraction = (value - minimum) / (maximum - minimum);
        return System.Math.Clamp(fraction, 0, 1) * trackWidth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
