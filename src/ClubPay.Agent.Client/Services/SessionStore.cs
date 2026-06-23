using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class SessionStore : ISessionStore
{
    public Session? Current { get; private set; }
    public void Save(Session session) => Current = session;
    public void Clear() => Current = null;
}
