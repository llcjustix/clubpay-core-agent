namespace ClubPay.Agent.Core.Services;

/// <summary>Detects genuine keyboard/mouse inactivity (as opposed to a flat timer) so idle→sleep policy
/// reflects real usage (ТЗ §8).</summary>
public interface IIdleDetectionService : IDisposable
{
    TimeSpan IdleThreshold { get; set; }

    /// <summary>Raised once when idle time first crosses IdleThreshold; resets once fresh input is seen.</summary>
    event Action? IdleThresholdReached;

    void Start();
    void Stop();
}
