using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>Persists the current session across process restarts (contract §1/§8: the agent must keep
/// running the local timer through to ends_at even after a crash/reboot).</summary>
public interface ISessionStore
{
    Session? Current { get; }
    Task SaveAsync(Session session, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    Task<Session?> LoadAsync(CancellationToken ct = default);
}
