using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace KlevaDeploy.Converters;

public sealed class IconVariantToImageSourceConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var primaryLight = values.ElementAtOrDefault(0) as string;
        var primaryDark = values.ElementAtOrDefault(1) as string;

        string? fallbackLight = null;
        string? fallbackDark = null;

        object? isDarkValue = values.ElementAtOrDefault(2);
        if (values.Length >= 5)
        {
            fallbackLight = values.ElementAtOrDefault(2) as string;
            fallbackDark = values.ElementAtOrDefault(3) as string;
            isDarkValue = values.ElementAtOrDefault(4);
        }

        var isDark = isDarkValue switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => false
        };

        var candidates = new[]
        {
            isDark ? primaryDark : primaryLight,
            isDark ? primaryLight : primaryDark,
            isDark ? fallbackDark : fallbackLight,
            isDark ? fallbackLight : fallbackDark
        };

        string? path = null;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var resolved = ResolvePath(candidate);
            if (string.IsNullOrWhiteSpace(resolved)) continue;

            if (Uri.TryCreate(resolved, UriKind.Absolute, out var packUri) &&
                string.Equals(packUri.Scheme, "pack", StringComparison.OrdinalIgnoreCase))
            {
                path = resolved;
                break;
            }

            if (File.Exists(resolved))
            {
                path = resolved;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(path)) return null;

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, "pack", StringComparison.OrdinalIgnoreCase))
        {
            return LoadPackImage(uri);
        }

        if (string.Equals(Path.GetExtension(path), ".ico", StringComparison.OrdinalIgnoreCase))
        {
            using var fs = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames
                .OrderByDescending(f => f.PixelWidth * f.PixelHeight)
                .FirstOrDefault();

            if (frame is null) return null;
            frame.Freeze();
            return frame;
        }

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static BitmapSource? LoadPackImage(Uri packUri)
    {
        try
        {
            var ext = Path.GetExtension(packUri.AbsolutePath);
            if (string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase))
            {
                var sri = Application.GetResourceStream(packUri);
                if (sri?.Stream is null) return null;
                using var fs = sri.Stream;
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames
                    .OrderByDescending(f => f.PixelWidth * f.PixelHeight)
                    .FirstOrDefault();
                if (frame is null) return null;
                frame.Freeze();
                return frame;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = packUri;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePath(string candidate)
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, "pack", StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

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
