using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Synthtax.Vsix.ToolWindow.Converters;

// ─── Severity → bakgrundsfärg (för dot/badge) ──────────────────────────────

[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public sealed class SeverityToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush CriticalBrush = new(Color.FromRgb(0xE7, 0x4C, 0x3C));
    private static readonly SolidColorBrush HighBrush     = new(Color.FromRgb(0xE6, 0x7E, 0x22));
    private static readonly SolidColorBrush MediumBrush   = new(Color.FromRgb(0xF3, 0x9C, 0x12));
    private static readonly SolidColorBrush LowBrush      = new(Color.FromRgb(0x34, 0x98, 0xDB));
    private static readonly SolidColorBrush DefaultBrush  = new(Color.FromRgb(0x95, 0xA5, 0xA6));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? s switch
        {
            "Critical" => CriticalBrush,
            "High"     => HighBrush,
            "Medium"   => MediumBrush,
            "Low"      => LowBrush,
            _          => DefaultBrush
        } : DefaultBrush;

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

// ─── Severity → textfärg ───────────────────────────────────────────────────

[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public sealed class SeverityToTextColorConverter : IValueConverter
{
    private static readonly SolidColorBrush CriticalText = new(Color.FromRgb(0xC0, 0x39, 0x2B));
    private static readonly SolidColorBrush HighText     = new(Color.FromRgb(0xCA, 0x6F, 0x1E));
    private static readonly SolidColorBrush MediumText   = new(Color.FromRgb(0xB7, 0x77, 0x0D));
    private static readonly SolidColorBrush DefaultText  = new(Color.FromRgb(0x2C, 0x3E, 0x50));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? s switch
        {
            "Critical" => CriticalText,
            "High"     => HighText,
            "Medium"   => MediumText,
            _          => DefaultText
        } : DefaultText;

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

// ─── Status → emoji-ikon ───────────────────────────────────────────────────

[ValueConversion(typeof(string), typeof(string))]
public sealed class StatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? s switch
        {
            "Open"          => "🔴",
            "Acknowledged"  => "🟡",
            "InProgress"    => "🔵",
            "Resolved"      => "✅",
            "Accepted"      => "✔️",
            "FalsePositive" => "🚫",
            _               => "⚪"
        } : "⚪";

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

// ─── Bool → Visibility ─────────────────────────────────────────────────────

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is Visibility vis && vis == Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is Visibility vis && vis == Visibility.Collapsed;
}

// ─── NYTT #8: IsAutoFixable → ⚡ eller tom sträng ────────────────────────────

[ValueConversion(typeof(bool), typeof(string))]
public sealed class AutoFixToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? "⚡" : string.Empty;

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

// ─── NYTT #10: Null/tom sträng → Collapsed ────────────────────────────────

[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class NullOrEmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

// ─── NYTT: int == 0 → Visible (för empty-state) ───────────────────────────

[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
