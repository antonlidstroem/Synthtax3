using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Synthtax.Core.Enums;

namespace Synthtax.WPF.Converters;

/// <summary>true → Visible, false → Collapsed</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>true → Collapsed, false → Visible</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not Visibility.Visible;
}

/// <summary>null or empty list → Visible (shows empty state), else Collapsed</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return Visibility.Visible;
        if (value is System.Collections.ICollection col && col.Count == 0) return Visibility.Visible;
        if (value is string s && string.IsNullOrWhiteSpace(s)) return Visibility.Visible;
        return Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts Severity enum → background brush for badges</summary>
public class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Severity severity)
        {
            return severity switch
            {
                Severity.Critical => new SolidColorBrush(Color.FromRgb(183, 28, 28)),
                Severity.High     => new SolidColorBrush(Color.FromRgb(229, 57, 53)),
                Severity.Medium   => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                Severity.Low      => new SolidColorBrush(Color.FromRgb(29, 179, 107)),
                _                 => new SolidColorBrush(Color.FromRgb(148, 163, 184))
            };
        }
        if (value is string str)
        {
            return str.ToLowerInvariant() switch
            {
                "critical" => new SolidColorBrush(Color.FromRgb(183, 28, 28)),
                "high"     => new SolidColorBrush(Color.FromRgb(229, 57, 53)),
                "medium"   => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                "low"      => new SolidColorBrush(Color.FromRgb(29, 179, 107)),
                _          => new SolidColorBrush(Color.FromRgb(148, 163, 184))
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converts Severity → foreground color for text on badges</summary>
public class SeverityToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new SolidColorBrush(Colors.White);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>MaintainabilityIndex double → color: green ≥ 70, yellow ≥ 40, red below</summary>
public class MaintainabilityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            if (d >= 70) return new SolidColorBrush(Color.FromRgb(29, 179, 107));
            if (d >= 40) return new SolidColorBrush(Color.FromRgb(245, 158, 11));
            return new SolidColorBrush(Color.FromRgb(229, 57, 53));
        }
        return new SolidColorBrush(Colors.Gray);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Formats DateTime → "dd MMM yyyy HH:mm"</summary>
public class DateTimeFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return dt.ToString(parameter as string ?? "dd MMM yyyy HH:mm");
        if (value is DateTimeOffset dto)
            return dto.LocalDateTime.ToString(parameter as string ?? "dd MMM yyyy HH:mm");
        return string.Empty;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>null → false, non-null → true (for IsEnabled bindings)</summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>int → string with thousands separator</summary>
public class NumberFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            int i    => i.ToString("N0"),
            long l   => l.ToString("N0"),
            double d => d.ToString("N1"),
            _        => value?.ToString() ?? string.Empty
        };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>AI score (0.0-1.0) → percentage string</summary>
public class ScoreToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? $"{d * 100:F0}%" : "–";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>AI score → color brush</summary>
public class ScoreToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            if (d >= 0.65) return new SolidColorBrush(Color.FromRgb(229, 57, 53));
            if (d >= 0.40) return new SolidColorBrush(Color.FromRgb(245, 158, 11));
            if (d >= 0.20) return new SolidColorBrush(Color.FromRgb(59, 125, 216));
            return new SolidColorBrush(Color.FromRgb(29, 179, 107));
        }
        return new SolidColorBrush(Colors.Gray);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}
