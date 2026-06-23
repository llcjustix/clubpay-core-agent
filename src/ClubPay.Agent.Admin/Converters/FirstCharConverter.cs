using System.Globalization;
using System.Windows.Data;

namespace ClubPay.Agent.Admin.Converters;

[ValueConversion(typeof(string), typeof(string))]
public sealed class FirstCharConverter : IValueConverter
{
    public static readonly FirstCharConverter Instance = new();

    public object Convert(object v, Type _, object __, CultureInfo ___) =>
        v is string s && s.Length > 0 ? s[0].ToString().ToUpper() : "?";

    public object ConvertBack(object v, Type _, object __, CultureInfo ___) =>
        throw new NotSupportedException();
}
