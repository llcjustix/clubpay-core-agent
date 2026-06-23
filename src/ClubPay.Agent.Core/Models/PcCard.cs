namespace ClubPay.Agent.Core.Models;

public class PcCard
{
    public required string PcId { get; init; }
    public required ZoneType Zone { get; init; }
    public required PcStatus Status { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public string TimeRemaining { get; init; } = string.Empty;
    public string ZoneLabel => Zone switch { ZoneType.Pro => "Pro", ZoneType.Vip => "VIP", _ => "Standart" };
}
