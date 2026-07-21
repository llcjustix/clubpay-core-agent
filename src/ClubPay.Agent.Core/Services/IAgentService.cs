using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>Agent identity/config and OS-level power actions. Session lifecycle lives in
/// ISessionCoordinator — this interface no longer owns any session state.</summary>
public interface IAgentService
{
    string PcId { get; }
    string ExternalPcId { get; }
    ZoneType Zone { get; }
    string ClubName { get; }
    string WifiSsid { get; }
    string WifiPassword { get; }
    string PaymentBaseUrl { get; }

    Task SleepAsync(CancellationToken ct = default);

    /// <summary>Prevents (or allows) the OS from idling the display/system to sleep on its own —
    /// used while a paid session is Active/Frozen so Windows' own power plan can't sleep the PC
    /// underneath a running session.</summary>
    void KeepAwake(bool keepAwake);
}
