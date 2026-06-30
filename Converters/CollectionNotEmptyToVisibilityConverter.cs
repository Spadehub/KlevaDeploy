using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KlevaDeploy.Converters;

public sealed class CollectionNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool inverse = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        var visible = value switch
        {
            null => false,
            int i => i > 0,
            ICollection c => c.Count > 0,
            IEnumerable e => e.GetEnumerator().MoveNext(),
            _ => false
        };

        if (inverse) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
