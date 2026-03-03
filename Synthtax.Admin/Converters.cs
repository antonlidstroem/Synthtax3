using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Synthtax.Admin;

/// <summary>True -> Collapsed, False -> Visible</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => (Visibility)value == Visibility.Collapsed;
}
