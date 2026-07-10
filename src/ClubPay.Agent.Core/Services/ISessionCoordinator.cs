using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>
/// Owns all PC-control business logic (session lifecycle, manager lock, repair mode, idempotency,
/// idle→sleep, resume-from-sleep re-sync). The only thing a ViewModel should depend on for state —
/// it never knows about wire formats (command_id, JSON) or transport.
/// Expected business rejections (e.g. repair mode active) are thrown as
/// <see cref="Exceptions.SessionCommandException"/>; the dispatcher maps those to error_code.
/// </summary>
public interface ISessionCoordinator : IAsyncDisposable
{
    AgentState State { get; }
    Session? CurrentSession { get; }
    bool IsManagerLocked { get; }
    bool IsRepairMode { get; }

    /// <summary>Grace-period deadline while State == Frozen; null otherwise. ViewModels use this to
    /// render the countdown — the coordinator alone decides when grace actually expires.</summary>
    DateTime? FrozenUntilUtc { get; }

    /// <summary>Raised whenever State/CurrentSession/IsManagerLocked/IsRepairMode changes — the only
    /// subscription a ViewModel needs.</summary>
    event Action? StateChanged;

    /// <summary>Raised at the 10/5/1-minute thresholds so the UI can show a warning banner.</summary>
    event Action<TimeLowEvent>? TimeLowRaised;

    /// <summary>Recovers persisted state (ending an already-expired session immediately) and starts the
    /// internal ticking/idle-detection/power-event wiring. Call once at app startup, before the
    /// Controller channel connects.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>IsDuplicate is true when grant_id was already applied — contract §7: status stays "ok"
    /// with the current remaining_seconds, but the dispatcher must also set error_code: duplicate.</summary>
    Task<(StartSessionResult Result, bool IsDuplicate)> StartSessionAsync(StartSessionPayload payload, CancellationToken ct = default);
    Task<(ExtendSessionResult Result, bool IsDuplicate)> ExtendSessionAsync(ExtendSessionPayload payload, CancellationToken ct = default);
    Task<EndSessionResult> EndSessionAsync(EndSessionPayload payload, CancellationToken ct = default);
    Task LockAsync(string? reason, CancellationToken ct = default);
    Task UnlockAsync(string? reason, CancellationToken ct = default);
    Task SetRepairModeAsync(bool on, CancellationToken ct = default);

    /// <summary>Contract §4.5: near no-op ack — see WakePayload remarks for why real wake can't be
    /// handled here.</summary>
    Task<EmptyResult> WakeAckAsync(CancellationToken ct = default);
    Task SleepAsync(CancellationToken ct = default);

    GetStatusResult GetStatus();
}
