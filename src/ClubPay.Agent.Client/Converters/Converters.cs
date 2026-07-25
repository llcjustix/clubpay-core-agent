using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ClubPay.Agent.Core;

namespace ClubPay.Agent.Client.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type _, object __, CultureInfo ___) =>
        value is Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___) =>
        value is not true;

    public object ConvertBack(object value, Type _, object __, CultureInfo ___) =>
        value is not true;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type _, object __, CultureInfo ___) =>
        value is not Visibility.Visible;
}

/// <summary>ТЗ §7 warning ladder colors: red at ≤1 min, yellow at ≤5 min, green otherwise. Brushes are
/// shared frozen instances — this converter runs on every timer tick.</summary>
[ValueConversion(typeof(int), typeof(Brush))]
public sealed class TimerToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Red = Frozen(0xFF, 0x3B, 0x5C);
    private static readonly SolidColorBrush Yellow = Frozen(0xFF, 0xD6, 0x0A);
    private static readonly SolidColorBrush Green = Frozen(0x00, 0xFF, 0x88);

    public object Convert(object value, Type _, object __, CultureInfo ___)
    {
        int seconds = value is int s ? s : 0;
        if (seconds <= Constants.Timer.WarnAt1Min) return Red;
        if (seconds <= Constants.Timer.WarnAt5Min) return Yellow;
        return Green;
    }

    public object ConvertBack(object value, Type _, object __, CultureInfo ___) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
