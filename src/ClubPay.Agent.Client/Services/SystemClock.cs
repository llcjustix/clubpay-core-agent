using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
