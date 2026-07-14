using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Owns the entire PC-control state machine: session lifecycle, idempotent start/extend/end, manager
/// lock, repair mode, time_low thresholds, grace-period expiry, idle→sleep and resume-from-sleep
/// re-sync. MainViewModel only ever observes <see cref="StateChanged"/> — it never decides anything.
/// </summary>
public sealed class SessionCoordinatorService : ISessionCoordinator
{
    private static readonly int[] TimeLowThresholds =
        [Constants.Timer.WarnAt10Min, Constants.Timer.WarnAt5Min, Constants.Timer.WarnAt1Min];

    private readonly ISessionStore _store;
    private readonly IGrantIdempotencyStore _idempotency;
    private readonly IControllerChannel _channel;
    private readonly IAgentService _agent;
    private readonly IKioskLockService _kioskLock;
    private readonly IProcessCleanupService _processCleanup;
    private readonly IIdleDetectionService _idle;
    private readonly ISystemClock _clock;
    private readonly ILogger<SessionCoordinatorService> _logger;

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly HashSet<int> _firedThresholds = [];
    private Timer? _ticker;
    private bool _isAsleep;
    private int _secondsSinceHeartbeat;

    public AgentState State { get; private set; } = AgentState.Locked;
    public Session? CurrentSession { get; private set; }
    public bool IsManagerLocked { get; private set; }
    public bool IsRepairMode { get; private set; }
    public DateTime? FrozenUntilUtc { get; private set; }

    public event Action? StateChanged;
    public event Action<TimeLowEvent>? TimeLowRaised;

    public SessionCoordinatorService(
        ISessionStore store,
        IGrantIdempotencyStore idempotency,
        IControllerChannel channel,
        IAgentService agent,
        IKioskLockService kioskLock,
        IProcessCleanupService processCleanup,
        IIdleDetectionService idle,
        ISystemClock clock,
        ILogger<SessionCoordinatorService> logger)
    {
        _store = store;
        _idempotency = idempotency;
        _channel = channel;
        _agent = agent;
        _kioskLock = kioskLock;
        _processCleanup = processCleanup;
        _idle = idle;
        _clock = clock;
        _logger = logger;

        _idle.IdleThresholdReached += OnIdleThresholdReached;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        CurrentSession = _store.Current;

        if (CurrentSession is { } session && session.EndsAtUtc is { } endsAt && _clock.UtcNow >= endsAt)
        {
            // Expired while the agent was down/asleep — end it locally before anything else connects.
            await _stateLock.WaitAsync(ct);
            try
            {
                await EndSessionCoreAsync(session, EndReason.TimeUp, ct);
            }
            finally
            {
                _stateLock.Release();
            }
        }
        else if (CurrentSession is not null)
        {
            State = AgentState.Active;
            _kioskLock.SetMode(KioskLockMode.Session);
        }

        if (State == AgentState.Locked)
            _idle.Start();

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _ticker = new Timer(OnTick, null, 1000, 1000);
    }

    public async Task<(StartSessionResult Result, bool IsDuplicate)> StartSessionAsync(StartSessionPayload payload, CancellationToken ct = default)
    {
        if (await _idempotency.HasAppliedAsync(payload.GrantId, ct))
        {
            var current = CurrentSession;
            var existingId = current?.CoreSessionId?.ToString("N") ?? payload.GrantId;
            return (new StartSessionResult(existingId, current?.RemainingSeconds(_clock.UtcNow) ?? 0), true);
        }

        await _stateLock.WaitAsync(ct);
        try
        {
            if (IsRepairMode)
                throw new SessionCommandException(ErrorCode.PcInRepair, "pc is in repair mode");
            if (State != AgentState.Locked)
                throw new SessionCommandException(ErrorCode.PcBusy, "pc already has an active session");

            var now = _clock.UtcNow;
            var coreSessionId = Guid.NewGuid();
            var zone = ParseZone(payload.Zone);
            var tariff = new Tariff(coreSessionId, payload.Zone ?? "Standard", zone, payload.GrantedSeconds / 60, 0);
            var session = new Session(
                Guid.NewGuid(), payload.ExternalPcId, tariff, payload.StartAt ?? now, payload.GrantedSeconds,
                CoreSessionId: coreSessionId, GrantId: payload.GrantId, EndsAtUtc: payload.EndsAt, Zone: payload.Zone);

            await _store.SaveAsync(session, ct);
            await _idempotency.RecordAppliedAsync(payload.GrantId, ct);

            CurrentSession = session;
            State = AgentState.Active;
            _firedThresholds.Clear();
            _idle.Stop();
            _kioskLock.SetMode(KioskLockMode.Session);
            _processCleanup.SnapshotBaseline();

            var coreIdText = coreSessionId.ToString("N");
            await PublishEventAsync(Constants.ControllerChannel.EventName.SessionStarted,
                new SessionStartedEvent(coreIdText, payload.ExternalPcId, payload.GrantId, payload.PaymentOrderId, session.StartedAtUtc), ct);
            await PublishPcStateChangedAsync(ct);
            RaiseStateChanged();

            return (new StartSessionResult(coreIdText, session.RemainingSeconds(now)), false);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<(ExtendSessionResult Result, bool IsDuplicate)> ExtendSessionAsync(ExtendSessionPayload payload, CancellationToken ct = default)
    {
        if (await _idempotency.HasAppliedAsync(payload.GrantId, ct))
            return (new ExtendSessionResult(CurrentSession?.RemainingSeconds(_clock.UtcNow) ?? 0), true);

        await _stateLock.WaitAsync(ct);
        try
        {
            var session = CurrentSession;
            if (session is null || State == AgentState.Locked)
                throw new SessionCommandException(ErrorCode.InvalidState, "no active/frozen session to extend");

            var now = _clock.UtcNow;

            // Extend from "time remaining now" (0 while Frozen), not the original granted budget —
            // otherwise wall-clock time already spent waiting in the Frozen grace period would
            // silently eat into (or exceed) the added seconds, re-expiring the session right after
            // a supposedly successful extend.
            var newRemaining = session.RemainingSeconds(now) + payload.AddedSeconds;
            var newEndsAt = now.AddSeconds(newRemaining);
            var updated = session with
            {
                GrantedSeconds = session.ElapsedSeconds(now) + newRemaining,
                EndsAtUtc = newEndsAt,
            };

            await _store.SaveAsync(updated, ct);
            await _idempotency.RecordAppliedAsync(payload.GrantId, ct);
            CurrentSession = updated;
            _firedThresholds.Clear();

            bool wasFrozen = State == AgentState.Frozen;
            State = AgentState.Active;
            FrozenUntilUtc = null;
            if (wasFrozen)
            {
                _idle.Stop();
                _kioskLock.SetMode(KioskLockMode.Session);
            }

            await PublishEventAsync(Constants.ControllerChannel.EventName.SessionExtended,
                new SessionExtendedEvent(payload.CoreSessionId, _agent.ExternalPcId, payload.GrantId, payload.AddedSeconds, newEndsAt), ct);
            await PublishPcStateChangedAsync(ct);
            RaiseStateChanged();

            return (new ExtendSessionResult(updated.RemainingSeconds(now)), false);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<EndSessionResult> EndSessionAsync(EndSessionPayload payload, CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            var session = CurrentSession;
            if (session is null)
                throw new SessionCommandException(ErrorCode.InvalidState, "no active session to end");

            return await EndSessionCoreAsync(session, payload.Reason, ct);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task LockAsync(string? reason, CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            IsManagerLocked = true;
            _kioskLock.SetMode(KioskLockMode.Full);
            await PublishPcStateChangedAsync(ct);
            RaiseStateChanged();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task UnlockAsync(string? reason, CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            IsManagerLocked = false;
            if (State == AgentState.Active)
                _kioskLock.SetMode(KioskLockMode.Session);
            await PublishPcStateChangedAsync(ct);
            RaiseStateChanged();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task SetRepairModeAsync(bool on, CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            IsRepairMode = on;
            await PublishPcStateChangedAsync(ct);
            RaiseStateChanged();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public Task<EmptyResult> WakeAckAsync(CancellationToken ct = default)
    {
        // See WakePayload remarks: if the PC were truly asleep the agent couldn't be receiving this at
        // all. Real Wake-on-LAN is a Controller-side, network-level concern outside this process.
        _logger.LogInformation("wake buyrug'i qabul qilindi — PC allaqachon uyg'oq");
        return Task.FromResult(new EmptyResult());
    }

    public async Task SleepAsync(CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            if (State != AgentState.Locked)
                throw new SessionCommandException(ErrorCode.PcBusy, "cannot sleep while a session is active");

            await SleepInternalAsync(ct);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public GetStatusResult GetStatus()
    {
        var now = _clock.UtcNow;
        var pcState = PcStateMapper.ToWireState(State, IsManagerLocked, IsRepairMode, _isAsleep, isConnected: true);
        SessionState? sessionState = CurrentSession is null
            ? null
            : State switch
            {
                AgentState.Active => SessionState.Active,
                AgentState.Frozen => SessionState.Frozen,
                _ => SessionState.Ended,
            };

        return new GetStatusResult(
            pcState,
            sessionState,
            CurrentSession?.CoreSessionId?.ToString("N"),
            CurrentSession?.RemainingSeconds(now));
    }

    public ValueTask DisposeAsync()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _idle.IdleThresholdReached -= OnIdleThresholdReached;
        _ticker?.Dispose();
        _idle.Stop();
        _stateLock.Dispose();
        return ValueTask.CompletedTask;
    }

    // ─── Internal state mutation (caller must already hold _stateLock, except *_ThresholdReached/tick) ──

    private async Task<EndSessionResult> EndSessionCoreAsync(Session session, EndReason reason, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        int consumed = session.ElapsedSeconds(now);
        int remaining = session.RemainingSeconds(now);

        _processCleanup.KillSessionProcesses();
        _kioskLock.SetMode(KioskLockMode.Full);
        await _store.ClearAsync(ct);

        CurrentSession = null;
        FrozenUntilUtc = null;
        State = AgentState.Locked;
        _firedThresholds.Clear();
        _idle.Start();

        var coreIdText = session.CoreSessionId?.ToString("N") ?? session.Id.ToString("N");
        await PublishEventAsync(Constants.ControllerChannel.EventName.SessionEnded,
            new SessionEndedEvent(coreIdText, _agent.ExternalPcId, reason, consumed, remaining), ct);
        await PublishPcStateChangedAsync(ct);
        RaiseStateChanged();

        return new EndSessionResult(consumed, remaining);
    }

    private async Task SleepInternalAsync(CancellationToken ct)
    {
        _idle.Stop();
        _isAsleep = true;
        RaiseStateChanged();
        await PublishPcStateChangedAsync(ct);

        try
        {
            await _agent.SleepAsync(ct);
        }
        finally
        {
            _isAsleep = false;
            if (State == AgentState.Locked)
                _idle.Start();
            RaiseStateChanged();
            await PublishPcStateChangedAsync(ct);
        }
    }

    private void OnIdleThresholdReached() => _ = HandleIdleTimeoutAsync();

    private async Task HandleIdleTimeoutAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (State != AgentState.Locked)
                return;

            await SleepInternalAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Idle-sleep ishga tushirishda xato");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;

        _ = ResumeFromSleepAsync();
    }

    private async Task ResumeFromSleepAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            _isAsleep = false;
            var session = CurrentSession;

            if (session is not null && session.EndsAtUtc is { } endsAt && _clock.UtcNow >= endsAt && State != AgentState.Locked)
            {
                await EndSessionCoreAsync(session, EndReason.TimeUp, CancellationToken.None);
                return;
            }

            if (State == AgentState.Locked)
                _idle.Start();
            RaiseStateChanged();
            await PublishPcStateChangedAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Uyg'onishdan keyin holatni tiklashda xato");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void OnTick(object? _) => _ = OnTickAsync();

    private async Task OnTickAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            var now = _clock.UtcNow;

            _secondsSinceHeartbeat++;
            if (_secondsSinceHeartbeat >= Constants.ControllerChannel.HeartbeatIntervalSeconds)
            {
                _secondsSinceHeartbeat = 0;
                await PublishHeartbeatAsync(CancellationToken.None);
            }

            if (State == AgentState.Active && CurrentSession is { } session)
            {
                foreach (var threshold in TimeLowThresholds)
                {
                    int remaining = session.RemainingSeconds(now);
                    if (remaining > 0 && remaining <= threshold && _firedThresholds.Add(threshold))
                    {
                        var coreIdText = session.CoreSessionId?.ToString("N") ?? session.Id.ToString("N");
                        var evt = new TimeLowEvent(coreIdText, remaining, threshold);
                        TimeLowRaised?.Invoke(evt);
                        await PublishEventAsync(Constants.ControllerChannel.EventName.TimeLow, evt, CancellationToken.None);
                    }
                }

                if (session.IsExpired(now))
                {
                    State = AgentState.Frozen;
                    FrozenUntilUtc = now.AddSeconds(Constants.Timer.GracePeriod);
                    _kioskLock.SetMode(KioskLockMode.Full);
                    RaiseStateChanged();
                    await PublishPcStateChangedAsync(CancellationToken.None);
                }
            }
            else if (State == AgentState.Frozen && CurrentSession is { } frozenSession && FrozenUntilUtc is { } until && now >= until)
            {
                await EndSessionCoreAsync(frozenSession, EndReason.TimeUp, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ichki timer ishlov berishda xato");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private Task PublishHeartbeatAsync(CancellationToken ct)
    {
        var wireState = PcStateMapper.ToWireState(State, IsManagerLocked, IsRepairMode, _isAsleep, isConnected: true);
        return PublishEventAsync(Constants.ControllerChannel.EventName.Heartbeat,
            new HeartbeatEvent(_agent.ExternalPcId, wireState, ControllersSeen: 1, ServerReachable: true), ct);
    }

    private async Task PublishPcStateChangedAsync(CancellationToken ct)
    {
        var wireState = PcStateMapper.ToWireState(State, IsManagerLocked, IsRepairMode, _isAsleep, isConnected: true);
        await PublishEventAsync(Constants.ControllerChannel.EventName.PcStateChanged,
            new PcStateChangedEvent(_agent.ExternalPcId, wireState, Online: true), ct);
    }

    private async Task PublishEventAsync(string name, object payload, CancellationToken ct)
    {
        try
        {
            await _channel.PublishEventAsync(name, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Event} hodisasini yuborib bo'lmadi", name);
        }
    }

    private void RaiseStateChanged()
    {
        _agent.KeepAwake(State != AgentState.Locked);
        StateChanged?.Invoke();
    }

    private static ZoneType ParseZone(string? zone) =>
        Enum.TryParse<ZoneType>(zone, ignoreCase: true, out var z) ? z : ZoneType.Standard;
}
