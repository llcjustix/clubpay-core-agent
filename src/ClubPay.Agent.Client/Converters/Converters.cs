using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ClubPay.Agent.Client.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type _, object __, CultureInfo ___) =>
        value is Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type _, object __, CultureInfo ___) =>
        value is not Visibility.Visible;
}

[ValueConversion(typeof(int), typeof(Brush))]
public sealed class TimerToBrushConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___)
    {
        int seconds = value is int s ? s : 0;
        return seconds switch
        {
            <= 60 => new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x5C)),  // red
            <= 300 => new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x0A)),  // orange
            <= 600 => new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A)),  // yellow
            _ => new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)),  // green
        };
    }

    public object ConvertBack(object value, Type _, object __, CultureInfo ___) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(int), typeof(double))]
public sealed class GraceToWidthConverter : IValueConverter
{
    public double TotalWidth { get; set; } = 520;

    public object Convert(object value, Type _, object parameter, CultureInfo ___)
    {
        if (value is not int remaining) return TotalWidth;
        int total = parameter is int p ? p : 600;
        return TotalWidth * Math.Clamp((double)remaining / total, 0, 1);
    }

    public object ConvertBack(object value, Type _, object __, CultureInfo ___) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(long), typeof(string))]
public sealed class TiyinToSomConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___)
    {
        if (value is not long tiyin) return "0 so'm";
        return $"{tiyin / 100m:N0} so'm";
    }

    public object ConvertBack(object value, Type _, object __, CultureInfo ___) =>
        throw new NotSupportedException();
}
