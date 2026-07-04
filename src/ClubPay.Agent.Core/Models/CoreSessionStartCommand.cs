namespace ClubPay.Agent.Core.Models;

public record CoreSessionStartCommand(
    Guid? CoreSessionId,
    string ExternalPcId,
    int GrantedSeconds,
    string? TariffName,
    long? PriceTiyin
);
