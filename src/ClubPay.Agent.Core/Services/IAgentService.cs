using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>Agent identity/config and OS-level power actions. Session lifecycle lives in
/// ISessionCoordinator — this interface no longer owns any session state.</summary>
public interface IAgentService
{
    string PcId { get; }
    string ExternalPcId { get; }
    ZoneType Zone { get; }
    string ZoneName { get; }
    string ClubName { get; }
    /// <summary>IANA or Windows timezone supplied by Core for player-facing clock display.</summary>
    string TimeZoneId { get; }
    string WifiSsid { get; }
    string WifiPassword { get; }
    /// <summary>The public static payment QR URL returned by Core bootstrap for this PC.</summary>
    string? StaticPaymentQrUrl { get; }
    event Action? StaticPaymentQrUrlChanged;
    /// <summary>Raised after Core bootstrap refreshes player-facing PC, club, or zone data.</summary>
    event Action? BootstrapChanged;

    /// <summary>Loads the static, backend-issued QR URL for this PC. A configured local fallback
    /// is retained if Core is temporarily unreachable.</summary>
    Task RefreshStaticPaymentQrUrlAsync(CancellationToken ct = default);

    Task SleepAsync(CancellationToken ct = default);

    /// <summary>Prevents (or allows) the OS from idling the display/system to sleep on its own —
    /// used while a paid session is Active/Frozen so Windows' own power plan can't sleep the PC
    /// underneath a running session.</summary>
    void KeepAwake(bool keepAwake);
}
