namespace ClubPay.Agent.Core.Services;

public interface IBillingEventReporter
{
    Task ReportPcStatusChangedAsync(string state, CancellationToken ct = default);
    Task ReportSessionStartedAsync(Guid sessionId, CancellationToken ct = default);
    Task ReportSessionEndedAsync(Guid sessionId, string? reason, CancellationToken ct = default);
    Task ReportSessionFailedAsync(Guid? sessionId, string reason, CancellationToken ct = default);
    Task ReportCommandFailedAsync(string command, string reason, CancellationToken ct = default);
}
