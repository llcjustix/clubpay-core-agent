using System.Globalization;

namespace ClubPay.Agent.Core;

/// <summary>CLAUDE.md: pul faqat long tiyinda saqlanadi va UI da shu klass orqali ko'rsatiladi —
/// "15 000 so'm" (guruh ajratgichi — bo'shliq, tiyin qismi ko'rsatilmaydi).</summary>
public static class MoneyFormatter
{
    private static readonly NumberFormatInfo SpaceGrouped = new()
    {
        NumberGroupSeparator = " ",
        NumberDecimalDigits = 0,
    };

    public static string Format(long tiyin)
    {
        long som = tiyin / Constants.Money.TiyinPerSom;
        return $"{som.ToString("N0", SpaceGrouped)} so'm";
    }
}
