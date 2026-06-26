using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

public interface IStartupSessionChecker
{
    Task<SessionStartCommand?> CheckAsync(CancellationToken ct = default);
}
