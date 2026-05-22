using System.Globalization;
using System.Windows.Data;

namespace KlevaDeploy.Converters;

/// <summary>
/// Parameter format: "NormalText|BusyText"
/// Returns NormalText when value is false, BusyText when true.
/// </summary>
public sealed class BusyToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "OK|Loading...").Split('|');
        return value is true ? parts[1] : parts[0];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
