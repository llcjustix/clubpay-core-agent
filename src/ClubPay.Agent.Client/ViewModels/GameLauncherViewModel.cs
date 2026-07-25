using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.ViewModels;

public partial class GameLauncherViewModel : ObservableObject
{
    // Steam (and similar launchers) hand off to an already-running client and exit almost
    // immediately — the real game keeps running under a different PID we never started directly.
    // We track "is anything foreign still running" via a process-diff against a baseline snapshot
    // taken right before launch, instead of trusting a single Process handle's exit.
    private const int PollIntervalSeconds = 3;
    private const int RequiredConsecutiveEmptyPolls = 2;
    private const int LaunchErrorVisibleSeconds = 6;

    public ObservableCollection<LauncherApp> Apps { get; } = [];

    [ObservableProperty] private LauncherApp? _runningApp;
    [ObservableProperty] private bool _isAppRunning;
    [ObservableProperty] private bool _isLaunchErrorVisible;
    [ObservableProperty] private string _launchErrorMessage = "";

    public event Action? ReturnRequested;   // show launcher window
    public event Action<LauncherApp> AppLaunched = delegate { }; // hide launcher, game visible

    private readonly IProcessCleanupService _processCleanup;
    private readonly ILogger<GameLauncherViewModel> _logger;
    private Process? _currentProcess;
    private IReadOnlySet<int> _launchBaseline = new HashSet<int>();
    private CancellationTokenSource? _pollCts;
    private CancellationTokenSource? _errorCts;

    public string ClubName { get; }
    public string PcId { get; }

    // Docked sidebar inside the shell (ТЗ §22: "остаток времени и баланс-виджет ... из shell").
    public ActiveSessionViewModel ActiveSession { get; }

    public GameLauncherViewModel(
        IConfiguration config,
        IAgentService agent,
        ActiveSessionViewModel activeSession,
        IProcessCleanupService processCleanup,
        ILogger<GameLauncherViewModel> logger)
    {
        ActiveSession = activeSession;
        _processCleanup = processCleanup;
        _logger = logger;
        ClubName = agent.ClubName;
        PcId = agent.PcId;

        var section = config.GetSection("Launcher:Apps");
        foreach (var item in section.GetChildren())
        {
            var app = new LauncherApp(
                Name: item["Name"] ?? "?",
                ExePath: item["ExePath"] ?? "",
                Args: item["Args"] ?? "",
                IconPath: item["IconPath"] ?? "",
                Category: item["Category"] ?? "O'yin");
            if (!string.IsNullOrEmpty(app.ExePath))
                Apps.Add(app);
        }
    }

    [RelayCommand]
    public async Task LaunchApp(LauncherApp app)
    {
        if (IsAppRunning)
        {
            if (RunningApp == app)
                AppLaunched(app); // same tile clicked again — bring it back to foreground, don't relaunch
            else
                ShowLaunchError($"{RunningApp?.Name} band — avval uni yoping");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = app.ExePath,
            Arguments = app.Args,
            UseShellExecute = true
        };

        _launchBaseline = _processCleanup.SnapshotProcessIds();

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to launch {AppName} ({ExePath})", app.Name, app.ExePath);
            ShowLaunchError($"{app.Name} ishga tushmadi");
            return;
        }

        _currentProcess = proc;
        RunningApp = app;
        IsAppRunning = true;
        AppLaunched(app);  // signal to hide launcher window

        _pollCts?.Cancel();
        var pollCts = new CancellationTokenSource();
        _pollCts = pollCts;
        await WaitUntilAppClosedAsync(proc, pollCts.Token);

        _currentProcess = null;
        RunningApp = null;
        IsAppRunning = false;
        ReturnRequested?.Invoke(); // game (and any handed-off child process) exited → show launcher again
    }

    /// <summary>Waits for the directly-started process to exit, then keeps polling the process-diff
    /// against <see cref="_launchBaseline"/> until nothing foreign remains for two consecutive
    /// checks (debounces the brief gap while a launcher like Steam hands off to the real game).</summary>
    private async Task WaitUntilAppClosedAsync(Process? initialProcess, CancellationToken ct)
    {
        try
        {
            if (initialProcess is not null)
                await initialProcess.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var consecutiveEmpty = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var foreign = _processCleanup.GetForeignProcessIds(_launchBaseline);
                consecutiveEmpty = foreign.Count == 0 ? consecutiveEmpty + 1 : 0;
                if (consecutiveEmpty >= RequiredConsecutiveEmptyPolls) return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Foreign-process poll failed while waiting for app to close");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [RelayCommand]
    public void ReturnToLauncher()
    {
        // Minimise whatever the launch handed off to (real game window may not be _currentProcess
        // itself — see WaitUntilAppClosedAsync) so the launcher appears in front.
        ForEachRunningWindow(hwnd => NativeLauncher.ShowWindow(hwnd, NativeLauncher.SW_MINIMIZE));
        ReturnRequested?.Invoke();
    }

    /// <summary>Reverse of the minimize above — brings whatever the launch handed off to back to
    /// the foreground (e.g. the user re-clicked the already-running app's tile, or a session
    /// resumed from Frozen while a game is still open). Safe to call with nothing running yet.</summary>
    public void BringRunningAppToForeground()
    {
        ForEachRunningWindow(hwnd =>
        {
            NativeLauncher.ShowWindow(hwnd, NativeLauncher.SW_RESTORE);
            NativeLauncher.SetForegroundWindow(hwnd);
        });
    }

    private void ForEachRunningWindow(Action<nint> action)
    {
        foreach (var pid in _processCleanup.GetForeignProcessIds(_launchBaseline))
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                if (!proc.HasExited && proc.MainWindowHandle != nint.Zero)
                    action(proc.MainWindowHandle);
            }
            catch { /* process may have exited between enumeration and access */ }
        }
    }

    public void KillRunningApp()
    {
        _pollCts?.Cancel();
        _errorCts?.Cancel();
        IsLaunchErrorVisible = false;
        try { _currentProcess?.Kill(entireProcessTree: true); }
        catch { }
        _currentProcess = null;
        RunningApp = null;
        IsAppRunning = false;
    }

    private void ShowLaunchError(string message)
    {
        _errorCts?.Cancel();
        var cts = new CancellationTokenSource();
        _errorCts = cts;
        LaunchErrorMessage = message;
        IsLaunchErrorVisible = true;
        _ = HideLaunchErrorAfterDelay(cts.Token);
    }

    private async Task HideLaunchErrorAfterDelay(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(LaunchErrorVisibleSeconds), ct);
            IsLaunchErrorVisible = false;
        }
        catch (OperationCanceledException) { }
    }
}

internal static class NativeLauncher
{
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);
}
