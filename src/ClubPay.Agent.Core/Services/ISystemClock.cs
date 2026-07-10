namespace ClubPay.Agent.Core.Services;

/// <summary>Abstraction over the current UTC time so timer/drift logic (grace expiry, time_low
/// thresholds) can be unit-tested deterministically without Thread.Sleep.</summary>
public interface ISystemClock
{
    DateTime UtcNow { get; }
}
