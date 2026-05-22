using System;
using System.Globalization;
using System.Windows.Data;

namespace KlevaDeploy.Converters;

/// <summary>
/// Converts an order number (10, 20, 30...) to a display number (1, 2, 3...).
/// </summary>
public sealed class OrderToDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int order && order > 0)
        {
            return (order / 10).ToString();
        }
        return "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
