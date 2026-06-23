namespace ClubPay.Agent.Core.Models;

public record Tariff(
    Guid Id,
    string Name,
    ZoneType Zone,
    int DurationMinutes,
    long PriceTiyin
)
{
    public decimal PriceSom => PriceTiyin / 100m;
    public string PriceLabel => $"{PriceSom:N0} so'm";

    public string DurationLabel => DurationMinutes switch
    {
        30 => "30 daqiqa",
        60 => "1 soat",
        120 => "2 soat",
        180 => "3 soat",
        300 => "5 soat",
        _ => $"{DurationMinutes} daqiqa"
    };

    public static Tariff[] DefaultStandard =>
    [
        new(Guid.NewGuid(), "30 daqiqa",  ZoneType.Standard, 30,  800_000),
        new(Guid.NewGuid(), "1 soat",     ZoneType.Standard, 60,  1_500_000),
        new(Guid.NewGuid(), "2 soat",     ZoneType.Standard, 120, 2_800_000),
        new(Guid.NewGuid(), "3 soat",     ZoneType.Standard, 180, 3_900_000),
        new(Guid.NewGuid(), "5 soat",     ZoneType.Standard, 300, 6_000_000),
    ];
}
