using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DeploymentApp.Converters;

/// <summary>
/// Returns <see cref="Visibility.Collapsed"/> when the bound bool is <c>true</c>,
/// and <see cref="Visibility.Visible"/> when it is <c>false</c>.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v != Visibility.Visible;
}
