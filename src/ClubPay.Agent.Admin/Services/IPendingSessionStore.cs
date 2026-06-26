using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Services;

public interface IPendingSessionStore
{
    void Set(string pcId, SessionStartCommand cmd);
    SessionStartCommand? Get(string pcId);
    void Clear(string pcId);
}
