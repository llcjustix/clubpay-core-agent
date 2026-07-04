namespace ClubPay.Agent.Core.Models;

public record CoreEvent(
    string EventType,
    string ExternalPcId,
    DateTime TimestampUtc,
    object Data
);
