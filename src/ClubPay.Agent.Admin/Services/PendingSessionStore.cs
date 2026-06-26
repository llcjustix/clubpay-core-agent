using System.Collections.Concurrent;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Services;

public sealed class PendingSessionStore : IPendingSessionStore
{
    private readonly ConcurrentDictionary<string, SessionStartCommand> _sessions = new();

    public void Set(string pcId, SessionStartCommand cmd) =>
        _sessions[pcId] = cmd;

    public SessionStartCommand? Get(string pcId) =>
        _sessions.TryGetValue(pcId, out var cmd) ? cmd : null;

    public void Clear(string pcId) =>
        _sessions.TryRemove(pcId, out _);
}
