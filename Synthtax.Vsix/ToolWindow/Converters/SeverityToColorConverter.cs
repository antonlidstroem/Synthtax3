using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Synthtax.Vsix.ToolWindow.Converters;

// ═══════════════════════════════════════════════════════════════════════════
// SeverityToColorConverter
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Mappar Synthtax-severity ("Critical"|"High"|"Medium"|"Low") till
/// en <see cref="SolidColorBrush"/> för indikator-pricken i DataGrid.
/// </summary>
[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public sealed class SeverityToColorConverter : IValueConverter
{
    // Paletten följer VS:s diagnostik-konventioner:
    //   Error   → röd      (#E74C3C)
    //   Warning → gul/orange (#E67E22)
    //   Info    → blå      (#3498DB)
    //   Hidden  → grå      (#95A5A6)

    private static readonly SolidColorBrush CriticalBrush =
        new(Color.FromRgb(0xE7, 0x4C, 0x3C)) { Opacity = 1 };
    private static readonly SolidColorBrush HighBrush =
        new(Color.FromRgb(0xE6, 0x7E, 0x22));
    private static readonly SolidColorBrush MediumBrush =
        new(Color.FromRgb(0xF3, 0x9C, 0x12));
    private static readonly SolidColorBrush LowBrush =
        new(Color.FromRgb(0x34, 0x98, 0xDB));
    private static readonly SolidColorBrush DefaultBrush =
        new(Color.FromRgb(0x95, 0xA5, 0xA6));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? s switch
        {
            "Critical" => CriticalBrush,
            "High"     => HighBrush,
            "Medium"   => MediumBrush,
            "Low"      => LowBrush,
            _          => DefaultBrush
        } : DefaultBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// ═══════════════════════════════════════════════════════════════════════════
// StatusToIconConverter
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Mappar BacklogStatus-sträng till en emoji-ikon för kolumnen "Status".
/// </summary>
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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// ═══════════════════════════════════════════════════════════════════════════
// InverseBoolToVisibilityConverter
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Inverterad BooleanToVisibilityConverter: false → Visible, true → Collapsed.
/// Används för att visa "Logga in"-panelen när IsLoggedIn är false.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Collapsed;
}

// ═══════════════════════════════════════════════════════════════════════════
// SeverityToBrushConverter  (TextBlock.Foreground-variant)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Ger en <see cref="SolidColorBrush"/> avsedd för Text-element
/// (något mörkare än indikator-brusharna för läsbarhet).
/// </summary>
[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public sealed class SeverityToTextColorConverter : IValueConverter
{
    private static readonly SolidColorBrush CriticalText =
        new(Color.FromRgb(0xC0, 0x39, 0x2B));
    private static readonly SolidColorBrush HighText =
        new(Color.FromRgb(0xCA, 0x6F, 0x1E));
    private static readonly SolidColorBrush MediumText =
        new(Color.FromRgb(0xB7, 0x77, 0x0D));
    private static readonly SolidColorBrush DefaultText =
        new(Color.FromRgb(0x2C, 0x3E, 0x50));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? s switch
        {
            "Critical" => CriticalText,
            "High"     => HighText,
            "Medium"   => MediumText,
            _          => DefaultText
        } : DefaultText;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
