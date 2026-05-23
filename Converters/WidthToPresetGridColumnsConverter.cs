using System.Globalization;
using System.Windows.Data;

namespace KlevaDeploy.Converters;

[ValueConversion(typeof(double), typeof(int))]
public sealed class WidthToPresetGridColumnsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var width = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0
        };

        if (width <= 0) return 4;

        if (width < 700) return 2;
        if (width < 1050) return 3;

        var computed = (int)Math.Floor(width / 320.0);
        return Math.Max(4, computed);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

