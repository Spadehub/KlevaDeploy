using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace KlevaDeploy.Converters;

public sealed class IconVariantToImageSourceConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var light = values.ElementAtOrDefault(0) as string;
        var dark = values.ElementAtOrDefault(1) as string;
        var isDark = values.ElementAtOrDefault(2) switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => false
        };

        var candidate = isDark ? dark : light;
        candidate = string.IsNullOrWhiteSpace(candidate) ? light : candidate;
        candidate = string.IsNullOrWhiteSpace(candidate) ? dark : candidate;
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        var path = Path.IsPathRooted(candidate)
            ? candidate
            : Path.Combine(AppContext.BaseDirectory, candidate);
        if (!File.Exists(path)) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
