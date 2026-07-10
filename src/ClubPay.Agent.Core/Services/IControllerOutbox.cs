using ClubPay.Agent.Core.Contracts;

namespace ClubPay.Agent.Core.Services;

/// <summary>Contract §1/§8: while disconnected, events the agent would have sent are queued locally and
/// flushed in order once the channel reconnects; the Controller dedups by event_id.</summary>
public interface IControllerOutbox
{
    Task EnqueueAsync(EventEnvelope evt, CancellationToken ct = default);
    Task<IReadOnlyList<EventEnvelope>> GetPendingAsync(CancellationToken ct = default);
    Task MarkSentAsync(string eventId, CancellationToken ct = default);
}
