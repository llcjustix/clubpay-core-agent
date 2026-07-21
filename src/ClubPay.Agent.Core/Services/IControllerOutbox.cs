using ClubPay.Agent.Core.Contracts;

namespace ClubPay.Agent.Core.Services;

/// <summary>Contract §1/§8: while disconnected, events the agent would have sent are queued locally and
/// flushed in order once the channel reconnects; the Controller dedups by event_id.</summary>
public interface IControllerOutbox
{
    /// <summary>Builds an <see cref="EventEnvelope"/> for the given event and enqueues it (telemetry
    /// events such as heartbeat/pc_state_changed are coalesced to the latest pending instance per contract
    /// §9 — money/session events are never coalesced or dropped). Signals <see cref="WaitForPendingAsync"/>
    /// on success.</summary>
    Task PublishEventAsync(string eventName, object payload, CancellationToken ct = default);

    Task EnqueueAsync(EventEnvelope evt, CancellationToken ct = default);
    Task<IReadOnlyList<EventEnvelope>> GetPendingAsync(CancellationToken ct = default);
    Task MarkSentAsync(string eventId, CancellationToken ct = default);

    /// <summary>Completes as soon as a new event is enqueued via <see cref="PublishEventAsync"/>, or after
    /// <paramref name="timeout"/> elapses — lets a send loop avoid hot-polling <see cref="GetPendingAsync"/>.</summary>
    Task WaitForPendingAsync(TimeSpan timeout, CancellationToken ct = default);
}
