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

    private readonly IAgentService          _agentService;
    private readonly IControllerListener    _listener;
    private readonly IKioskLockService      _kioskLock;
    private readonly IProcessCleanupService _processCleanup;
    private readonly GameLauncherViewModel  _launcher;
    private DispatcherTimer?                _idleTimer;

    public MainViewModel(
        LockScreenViewModel    lockScreen,
        ActiveSessionViewModel activeSession,
        FreezeViewModel        freeze,
        IAgentService          agentService,
        IControllerListener    listener,
        IKioskLockService      kioskLock,
        IProcessCleanupService processCleanup,
        GameLauncherViewModel  launcher)
    {
        LockScreen      = lockScreen;
        ActiveSession   = activeSession;
        Freeze          = freeze;
        _agentService   = agentService;
        _listener       = listener;
        _kioskLock      = kioskLock;
        _processCleanup = processCleanup;
        _launcher       = launcher;

        LockScreen.SessionRequested    += OnSessionStarted;
        ActiveSession.FreezeRequested  += OnFreezeStarted;
        Freeze.ResumeRequested         += OnSessionResumed;
        Freeze.ExpiredRequested        += OnSessionExpired;

        _listener.SessionStartReceived += OnControllerSessionStart;
        _listener.SessionEndReceived   += OnSessionExpired;
    }

    private void OnControllerSessionStart(SessionStartCommand cmd)
    {
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

    private void OnSessionStarted(Session session)
    {
        _idleTimer?.Stop();

        // Snapshot running processes — anything started after this point
        // will be cleaned up when the session ends
        _processCleanup.SnapshotBaseline();

        // Switch to session lock: Win+TaskMgr blocked, but Alt+Tab/Alt+F4 allowed for games
        _kioskLock.SetMode(KioskLockMode.Session);

        ActiveSession.Load(session);
        State = AgentState.Active;
    }

    private void OnFreezeStarted()
    {
        // Restore full lock when frozen — user shouldn't touch the PC during grace
        _kioskLock.SetMode(KioskLockMode.Full);
        Freeze.StartGrace();
        State = AgentState.Frozen;
    }

    private void OnSessionResumed(int additionalSeconds)
    {
        _idleTimer?.Stop();
        _kioskLock.SetMode(KioskLockMode.Session);
        ActiveSession.Extend(additionalSeconds);
        State = AgentState.Active;
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
