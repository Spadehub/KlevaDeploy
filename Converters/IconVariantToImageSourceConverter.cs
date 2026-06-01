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
        var path = ResolvePath(candidate);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static string? ResolvePath(string candidate)
    {
        if (Path.IsPathRooted(candidate))
            return candidate;

        var baseDir = AppContext.BaseDirectory;
        var fromBase = Path.Combine(baseDir, candidate);
        if (File.Exists(fromBase)) return fromBase;

        var fromCwd = Path.Combine(Directory.GetCurrentDirectory(), candidate);
        if (File.Exists(fromCwd)) return fromCwd;

        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 6 && dir.Parent is not null; i++)
        {
            dir = dir.Parent;
            var probe = Path.Combine(dir.FullName, candidate);
            if (File.Exists(probe)) return probe;
        }

        return null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
