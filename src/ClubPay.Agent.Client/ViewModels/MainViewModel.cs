using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.ViewModels;

public enum AgentState { Locked, Active, Frozen }

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked), nameof(IsActive), nameof(IsFrozen))]
    private AgentState _state = AgentState.Locked;

    public bool IsLocked => State == AgentState.Locked;
    public bool IsActive => State == AgentState.Active;
    public bool IsFrozen => State == AgentState.Frozen;

    public LockScreenViewModel    LockScreen    { get; }
    public ActiveSessionViewModel ActiveSession { get; }
    public FreezeViewModel        Freeze        { get; }

    private readonly IAgentService            _agentService;
    private readonly IControllerListener      _listener;
    private readonly ICoreApiListener         _coreListener;
    private readonly IBillingEventReporter    _billingReporter;
    private readonly IKioskLockService        _kioskLock;
    private readonly IProcessCleanupService   _processCleanup;
    private readonly GameLauncherViewModel    _launcher;
    private readonly IStartupSessionChecker  _startupChecker;
    private DispatcherTimer?                  _idleTimer;
    private Session?                          _activeSession;

    public MainViewModel(
        LockScreenViewModel    lockScreen,
        ActiveSessionViewModel activeSession,
        FreezeViewModel        freeze,
        IAgentService          agentService,
        IControllerListener    listener,
        ICoreApiListener       coreListener,
        IBillingEventReporter  billingReporter,
        IKioskLockService      kioskLock,
        IProcessCleanupService processCleanup,
        GameLauncherViewModel  launcher,
        IStartupSessionChecker startupChecker)
    {
        LockScreen       = lockScreen;
        ActiveSession    = activeSession;
        Freeze           = freeze;
        _agentService    = agentService;
        _listener        = listener;
        _coreListener    = coreListener;
        _billingReporter = billingReporter;
        _kioskLock       = kioskLock;
        _processCleanup  = processCleanup;
        _launcher        = launcher;
        _startupChecker  = startupChecker;

        LockScreen.SessionRequested    += OnSessionStarted;
        ActiveSession.FreezeRequested  += OnFreezeStarted;
        Freeze.ResumeRequested         += OnSessionResumed;
        Freeze.ExpiredRequested        += OnSessionExpired;

        _listener.SessionStartReceived += OnControllerSessionStart;
        _listener.SessionEndReceived   += OnSessionExpired;

        _coreListener.SessionStartReceived  += OnCoreSessionStart;
        _coreListener.SessionExtendReceived += OnCoreSessionExtend;
        _coreListener.SessionEndReceived    += OnCoreSessionEnd;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var cmd = await _startupChecker.CheckAsync(ct);
            if (cmd is null)
                return;
            OnControllerSessionStart(cmd);
        }
        catch (OperationCanceledException) { }
    }

    private void OnControllerSessionStart(SessionStartCommand cmd)
    {
        if (State != AgentState.Locked)
            return;

        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var tariff  = new Tariff(cmd.SessionId, cmd.TariffName,
                _agentService.Zone, cmd.GrantedSeconds / 60, cmd.PriceTiyin);
            var session = new Session(cmd.SessionId, cmd.PcId,
                tariff, DateTime.UtcNow, cmd.GrantedSeconds);
            await _agentService.StartSessionAsync(session);
            OnSessionStarted(session);
        });
    }

    private void OnCoreSessionStart(CoreSessionStartCommand cmd)
    {
        if (State != AgentState.Locked)
        {
            _ = _billingReporter.ReportCommandFailedAsync("sessions/start", "pc is not locked");
            return;
        }

        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var sessionId = cmd.CoreSessionId ?? Guid.NewGuid();
            var tariff  = new Tariff(sessionId, cmd.TariffName ?? "Billing",
                _agentService.Zone, cmd.GrantedSeconds / 60, cmd.PriceTiyin ?? 0);
            var session = new Session(sessionId, _agentService.PcId,
                tariff, DateTime.UtcNow, cmd.GrantedSeconds, cmd.CoreSessionId ?? sessionId);
            await _agentService.StartSessionAsync(session);
            OnSessionStarted(session);
        });
    }

    private void OnCoreSessionExtend(CoreSessionExtendCommand cmd)
    {
        if (State == AgentState.Locked)
        {
            _ = _billingReporter.ReportCommandFailedAsync("sessions/extend", "pc is locked");
            return;
        }

        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await _agentService.ExtendSessionAsync(cmd.AdditionalSeconds);
            OnSessionResumed(cmd.AdditionalSeconds);
        });
    }

    private void OnCoreSessionEnd(CoreSessionEndCommand cmd) => OnSessionExpired();

    private void OnSessionStarted(Session session)
    {
        _idleTimer?.Stop();
        _activeSession = session;

        // Snapshot running processes — anything started after this point
        // will be cleaned up when the session ends
        _processCleanup.SnapshotBaseline();

        // Switch to session lock: Win+TaskMgr blocked, but Alt+Tab/Alt+F4 allowed for games
        _kioskLock.SetMode(KioskLockMode.Session);

        ActiveSession.Load(session);
        State = AgentState.Active;

        _ = _billingReporter.ReportSessionStartedAsync(session.Id);
        _ = _billingReporter.ReportPcStatusChangedAsync("Active");
    }

    private void OnFreezeStarted()
    {
        // Restore full lock when frozen — user shouldn't touch the PC during grace
        _kioskLock.SetMode(KioskLockMode.Full);
        Freeze.StartGrace();
        State = AgentState.Frozen;

        _ = _billingReporter.ReportPcStatusChangedAsync("Frozen");
    }

    private void OnSessionResumed(int additionalSeconds)
    {
        _idleTimer?.Stop();
        _kioskLock.SetMode(KioskLockMode.Session);
        ActiveSession.Extend(additionalSeconds);
        State = AgentState.Active;

        _ = _billingReporter.ReportPcStatusChangedAsync("Active");
    }

    private void OnSessionExpired()
    {
        _launcher.KillRunningApp();
        // Kill games and apps the user opened during the session
        _processCleanup.KillSessionProcesses();

        _kioskLock.SetMode(KioskLockMode.Full);
        LockScreen.Reset();
        State = AgentState.Locked;
        StartIdleTimer();

        if (_activeSession is { } session)
        {
            _ = _billingReporter.ReportSessionEndedAsync(session.Id, null);
            _activeSession = null;
        }
        _ = _billingReporter.ReportPcStatusChangedAsync("Locked");
    }

    private void StartIdleTimer()
    {
        _idleTimer = new DispatcherTimer(DispatcherPriority.Background)
            { Interval = TimeSpan.FromSeconds(Constants.Timer.IdleSleep) };
        _idleTimer.Tick += async (_, _) =>
        {
            _idleTimer!.Stop();
            await _agentService.SleepAsync();
        };
        _idleTimer.Start();
    }
}
