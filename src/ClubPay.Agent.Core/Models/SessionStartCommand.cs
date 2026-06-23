namespace ClubPay.Agent.Core.Models;

public record SessionStartCommand(
    Guid SessionId,
    string PcId,
    int GrantedSeconds,
    string TariffName,
    long PriceTiyin
);
