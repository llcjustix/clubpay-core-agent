using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type _, object __, CultureInfo ___) =>
        v is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type _, object __, CultureInfo ___) =>
        v is Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type _, object __, CultureInfo ___) =>
        v is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type _, object __, CultureInfo ___) =>
        v is not Visibility.Visible;
}

[ValueConversion(typeof(PcStatus), typeof(Brush))]
public sealed class PcStatusToBrushConverter : IValueConverter
{
    public object Convert(object v, Type _, object __, CultureInfo ___)
    {
        return (PcStatus)v switch
        {
            PcStatus.Active => new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)),
            PcStatus.Frozen => new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x5C)),
            PcStatus.Free => new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xFF)),
            PcStatus.Attention => new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A)),
            _ => new SolidColorBrush(Color.FromRgb(0x6F, 0x77, 0x87)),
        };
    }
    public object ConvertBack(object v, Type _, object __, CultureInfo ___) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(PcStatus), typeof(string))]
public sealed class PcStatusToTextConverter : IValueConverter
{
    public object Convert(object v, Type _, object __, CultureInfo ___) =>
        (PcStatus)v switch
        {
            PcStatus.Active => "Faol",
            PcStatus.Frozen => "Muzlatilgan",
            PcStatus.Free => "Bo'sh",
            PcStatus.Attention => "Diqqat",
            _ => "Uxlayapti"
        };
    public object ConvertBack(object v, Type _, object __, CultureInfo ___) =>
        throw new NotSupportedException();
}

[ValueConversion(typeof(long), typeof(string))]
public sealed class TiyinToSomConverter : IValueConverter
{
    public object Convert(object v, Type _, object __, CultureInfo ___) =>
        v is long t ? $"{t / 100m:N0} so'm" : "0 so'm";
    public object ConvertBack(object v, Type _, object __, CultureInfo ___) =>
        throw new NotSupportedException();
}
