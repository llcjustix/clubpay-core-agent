using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>Persists money-affecting agent changes atomically: session, replay key and outbox event.</summary>
public interface IAtomicAgentStateStore
{
    Task CommitStartAsync(Session session, string grantId, EventEnvelope sessionStarted, CancellationToken ct = default);
    Task CommitExtendAsync(Session session, string grantId, EventEnvelope sessionExtended, CancellationToken ct = default);
    Task CommitEndAsync(EventEnvelope sessionEnded, CancellationToken ct = default);
}
