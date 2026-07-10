using CommunityToolkit.Mvvm.ComponentModel;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked), nameof(IsActive), nameof(IsFrozen))]
    private AgentState _state = AgentState.Locked;

    [ObservableProperty] private bool _isManagerLocked;
    [ObservableProperty] private bool _isRepairMode;

    public bool IsLocked => State == AgentState.Locked;
    public bool IsActive => State == AgentState.Active;
    public bool IsFrozen => State == AgentState.Frozen;

    public LockScreenViewModel LockScreen { get; }
    public ActiveSessionViewModel ActiveSession { get; }
    public FreezeViewModel Freeze { get; }

    private readonly ISessionCoordinator _coordinator;
    private readonly GameLauncherViewModel _launcher;

    public MainViewModel(
        LockScreenViewModel lockScreen,
        ActiveSessionViewModel activeSession,
        FreezeViewModel freeze,
        ISessionCoordinator coordinator,
        GameLauncherViewModel launcher)
    {
        LockScreen = lockScreen;
        ActiveSession = activeSession;
        Freeze = freeze;
        _coordinator = coordinator;
        _launcher = launcher;

        _coordinator.StateChanged += OnCoordinatorStateChanged;
    }

    /// <summary>Session recovery (persisted state, already-expired-while-down handling) happens inside
    /// ISessionCoordinator.StartAsync, called from App.xaml.cs before this ViewModel is shown — this
    /// just reflects whatever state that produced.</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        RefreshFromCoordinator();
        return Task.CompletedTask;
    }

    private void OnCoordinatorStateChanged()
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(RefreshFromCoordinator);
    }

    private void RefreshFromCoordinator()
    {
        var wasLocked = State == AgentState.Locked;
        var newState = _coordinator.State;

        if (newState == AgentState.Locked && !wasLocked)
        {
            // Transitioned to Locked — kill whatever game the user had running.
            // Tech debt (pre-existing, out of scope for this pass): GameLauncherViewModel mixes
            // window/UI concerns with process-kill business logic.
            _launcher.KillRunningApp();
            ActiveSession.Stop();
            Freeze.Stop();
            LockScreen.Reset();
        }

        if (newState == AgentState.Active && _coordinator.CurrentSession is { } session)
        {
            ActiveSession.Sync(session);
        }

        if (newState == AgentState.Frozen && _coordinator.FrozenUntilUtc is { } until)
        {
            Freeze.ShowGrace(until, _coordinator.CurrentSession?.Id.ToString("N"));
        }

        State = newState;
        IsManagerLocked = _coordinator.IsManagerLocked;
        IsRepairMode = _coordinator.IsRepairMode;
    }
}
