using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nudge.App.Converters;

/// <summary>
/// Picks what the customization page's image preview shows: a newly-picked local file (values[0],
/// TableCustomizationViewModel.CustomImagePath) takes priority since it's what will actually be
/// saved, falling back to whatever the tile already has loaded (values[1], TableTileViewModel.Artwork)
/// so the preview isn't blank just because nothing new has been picked yet.
///
/// ConverterParameter="PlaceholderVisibility" switches this to answer a different question from the
/// exact same two inputs - "should the 'No image yet' caption show?" - rather than duplicating the
/// same priority logic in a second converter (or, worse, a computed view-model property that would
/// need to separately watch both CustomImagePath and the tile's own Artwork changing to stay correct).
/// Without this, that caption was unconditionally part of the Border's content and rendered on top of
/// the image the whole time, not just when there wasn't one.
/// </summary>
public sealed class TableCustomizationPreviewConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool wantsVisibility = (parameter as string) == "PlaceholderVisibility";
        ImageSource? resolved = ResolveImage(values);

        if (wantsVisibility)
        {
            return resolved is null ? Visibility.Visible : Visibility.Collapsed;
        }

        return resolved;
    }

    private static ImageSource? ResolveImage(object[] values)
    {
        if (values.Length > 0 && values[0] is string path && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                // Falls through to whatever the tile already has - an unreadable/invalid path
                // shouldn't blank out a preview that was working a moment ago.
            }
        }

        return values.Length > 1 ? values[1] as ImageSource : null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
