using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

public interface IAgentService
{
    string PcId { get; }
    ZoneType Zone { get; }
    string ClubName { get; }
    string WifiSsid { get; }

    Task<bool> StartSessionAsync(Session session, CancellationToken ct = default);
    Task EndSessionAsync(CancellationToken ct = default);
    Task ExtendSessionAsync(int additionalSeconds, CancellationToken ct = default);
    Task SleepAsync(CancellationToken ct = default);
}

public interface IVoucherService
{
    VoucherToken? Redeem(string code, string pcId);
    bool Validate(VoucherToken token, string pcId, DateTime nowUtc);
}

public interface ISessionStore
{
    Session? Current { get; }
    void Save(Session session);
    void Clear();
}
