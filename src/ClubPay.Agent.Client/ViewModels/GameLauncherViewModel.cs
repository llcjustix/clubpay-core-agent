using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
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
    private readonly HashSet<int> _ownedProcessIds = [];
    private string? _ownedModuleDirectory;
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
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in section.GetChildren())
        {
            var type = ParseAppType(item["Type"]);
            int? steamAppId = int.TryParse(item["SteamAppId"], out var parsedSteamAppId)
                ? parsedSteamAppId
                : null;
            var processNames = item.GetSection("ProcessNames").GetChildren()
                .Select(x => x.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var configuredArgs = item["Args"] ?? "";
            var args = type == LauncherAppType.SteamGame
                ? RemoveLegacySteamLaunchArgument(configuredArgs)
                : configuredArgs;
            if (type == LauncherAppType.SteamGame && !string.Equals(configuredArgs, args, StringComparison.Ordinal))
                _logger.LogWarning("Launcher profile {ProfileName} uses legacy -applaunch arguments; SteamAppId is authoritative", item["Name"]);

            var app = new LauncherApp(
                Name: item["Name"] ?? "?",
                ExePath: item["ExePath"] ?? "",
                Args: args,
                IconPath: item["IconPath"] ?? "",
                Category: item["Category"] ?? "O'yin",
                Id: item["Id"] ?? "",
                Type: type,
                SteamAppId: steamAppId,
                WorkingDirectory: item["WorkingDirectory"] ?? "",
                ProcessNames: processNames);

            if (!IsValidProfile(app))
            {
                _logger.LogWarning("Ignoring invalid launcher profile {ProfileName}", app.Name);
                continue;
            }
            if (!seenIds.Add(app.StableId))
            {
                _logger.LogWarning("Ignoring launcher profile {ProfileName} with duplicate Id {StableId}", app.Name, app.StableId);
                continue;
            }
            Apps.Add(app);
        }
    }

    [RelayCommand]
    public async Task LaunchApp(LauncherApp app)
    {
        Debug.Assert(
            System.Windows.Application.Current?.Dispatcher.CheckAccess() ?? true,
            "GameLauncherViewModel accessed off the UI dispatcher thread");

        if (IsAppRunning)
        {
            if (RunningApp == app)
                AppLaunched(app); // same tile clicked again — bring it back to foreground, don't relaunch
            else
                ShowLaunchError($"{RunningApp?.Name} band — avval uni yoping");
            return;
        }

        var processNames = app.ProcessNames ?? new HashSet<string>();
        var moduleDirectory = app.Type == LauncherAppType.SteamGame ? null : Path.GetDirectoryName(app.ExePath);

        if (app.Type == LauncherAppType.Browser && processNames.Count > 0
            && TryAdoptRunningInstance(app, processNames, moduleDirectory, out var adoptTask))
        {
            await adoptTask;
            return;
        }

        var psi = CreateStartInfo(app);

        _launchBaseline = _processCleanup.SnapshotProcessIds();
        _ownedModuleDirectory = moduleDirectory;
        _ownedProcessIds.Clear();

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
        if (proc is not null)
            _ownedProcessIds.Add(proc.Id);
        RunningApp = app;
        IsAppRunning = true;
        AppLaunched(app);  // signal to hide launcher window

        await RunPollLoopAndFinishAsync(proc, processNames);
    }

    /// <summary>Single-instance handoff (e.g. Chrome): a fresh Process.Start would just forward the
    /// command line to the already-running instance via IPC and exit almost immediately, and PID-diff
    /// would never see a "new" process since the real one predates our baseline — so if one is already
    /// running we adopt it directly instead of relaunching. Returns false (nothing to await) when no
    /// running instance was found, so the caller falls through to a normal launch.</summary>
    private bool TryAdoptRunningInstance(
        LauncherApp app, IReadOnlySet<string> processNames, string? moduleDirectory, out Task adoptTask)
    {
        var existingPids = _processCleanup.GetProcessIdsStartedAfterBaseline(new HashSet<int>(), processNames, moduleDirectory);
        if (existingPids.Count == 0)
        {
            adoptTask = Task.CompletedTask;
            return false;
        }

        _launchBaseline = _processCleanup.SnapshotProcessIds();
        _ownedModuleDirectory = moduleDirectory;
        _ownedProcessIds.Clear();
        foreach (var pid in existingPids)
            _ownedProcessIds.Add(pid);

        _currentProcess = null;
        RunningApp = app;
        IsAppRunning = true;
        AppLaunched(app);

        adoptTask = RunPollLoopAndFinishAsync(initialProcess: null, processNames);
        return true;
    }

    /// <summary>Runs the close-detection poll loop to completion, then resets running-app state.
    /// <see cref="ReturnRequested"/> only fires when the wait ended because the app was genuinely
    /// closed — not when it was externally cancelled (KillRunningApp, or superseded by a new
    /// LaunchApp call), since whichever caller cancelled the token already owns the UI transition.</summary>
    private async Task RunPollLoopAndFinishAsync(Process? initialProcess, IReadOnlySet<string> processNames)
    {
        CancelAndDispose(ref _pollCts);
        var pollCts = new CancellationTokenSource();
        _pollCts = pollCts;

        var closedNaturally = await WaitUntilAppClosedAsync(initialProcess, processNames, pollCts.Token);

        if (ReferenceEquals(_pollCts, pollCts))
            CancelAndDispose(ref _pollCts);
        else
            pollCts.Dispose(); // already superseded by a newer LaunchApp/adoption call

        _currentProcess = null;
        RunningApp = null;
        IsAppRunning = false;
        if (closedNaturally)
            ReturnRequested?.Invoke(); // game (and any handed-off child process) exited → show launcher again
    }

    /// <summary>Tracks the starter process and only process names declared by the active profile.
    /// Two consecutive empty checks debounce the brief gap while Steam hands off to the real game.
    /// Returns true if the app was found to be genuinely closed, false if the wait was cancelled
    /// externally (kill or supersede) before that could be determined.</summary>
    private async Task<bool> WaitUntilAppClosedAsync(
        Process? initialProcess,
        IReadOnlySet<string> allowedProcessNames,
        CancellationToken ct)
    {
        var consecutiveEmpty = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var pid in _processCleanup.GetProcessIdsStartedAfterBaseline(_launchBaseline, allowedProcessNames, _ownedModuleDirectory))
                    _ownedProcessIds.Add(pid);

                bool initialStillRunning = initialProcess is not null && !initialProcess.HasExited;
                var runningOwned = _ownedProcessIds.Where(IsProcessRunning).ToArray();
                consecutiveEmpty = !initialStillRunning && runningOwned.Length == 0
                    ? consecutiveEmpty + 1
                    : 0;
                if (consecutiveEmpty >= RequiredConsecutiveEmptyPolls) return true;
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
                return false;
            }
        }

        return false;
    }

    [RelayCommand]
    public void ReturnToLauncher()
    {
        Debug.Assert(
            System.Windows.Application.Current?.Dispatcher.CheckAccess() ?? true,
            "GameLauncherViewModel accessed off the UI dispatcher thread");

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
        Debug.Assert(
            System.Windows.Application.Current?.Dispatcher.CheckAccess() ?? true,
            "GameLauncherViewModel accessed off the UI dispatcher thread");

        ForEachRunningWindow(hwnd =>
        {
            NativeLauncher.ShowWindow(hwnd, NativeLauncher.SW_RESTORE);
            NativeLauncher.SetForegroundWindow(hwnd);
        });
    }

    private void ForEachRunningWindow(Action<nint> action)
    {
        foreach (var pid in GetRunningOwnedProcessIds())
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
        Debug.Assert(
            System.Windows.Application.Current?.Dispatcher.CheckAccess() ?? true,
            "GameLauncherViewModel accessed off the UI dispatcher thread");

        CancelAndDispose(ref _pollCts);
        CancelAndDispose(ref _errorCts);
        IsLaunchErrorVisible = false;
        _processCleanup.KillProcesses(GetRunningOwnedProcessIds());
        _currentProcess = null;
        RunningApp = null;
        IsAppRunning = false;
    }

    /// <summary>Cancels then disposes a CancellationTokenSource field and nulls it out in one
    /// synchronous step, so any later `field?.Cancel()` against the (now-null) field is a safe
    /// no-op instead of an ObjectDisposedException.</summary>
    private static void CancelAndDispose(ref CancellationTokenSource? field)
    {
        field?.Cancel();
        field?.Dispose();
        field = null;
    }

    private static bool IsValidProfile(LauncherApp app) =>
        app.Type switch
        {
            LauncherAppType.SteamGame => !string.IsNullOrWhiteSpace(app.ExePath)
                && app.SteamAppId is > 0
                && app.ProcessNames is { Count: > 0 },
            _ => !string.IsNullOrWhiteSpace(app.ExePath),
        };

    private static LauncherAppType ParseAppType(string? value) =>
        Enum.TryParse<LauncherAppType>(value, ignoreCase: true, out var type)
            ? type
            : LauncherAppType.Executable;

    private static ProcessStartInfo CreateStartInfo(LauncherApp app)
    {
        var args = app.Type == LauncherAppType.SteamGame
            ? $"-applaunch {app.SteamAppId} {app.Args}".Trim()
            : app.Args;

        return new ProcessStartInfo
        {
            FileName = app.ExePath,
            Arguments = args,
            WorkingDirectory = string.IsNullOrWhiteSpace(app.WorkingDirectory)
                ? Path.GetDirectoryName(app.ExePath) ?? ""
                : app.WorkingDirectory,
            UseShellExecute = true,
        };
    }

    private static string RemoveLegacySteamLaunchArgument(string args) =>
        Regex.Replace(args, @"(?<!\S)-applaunch\s+\d+(?!\S)", "", RegexOptions.IgnoreCase).Trim();

    private IReadOnlyCollection<int> GetRunningOwnedProcessIds()
    {
        foreach (var pid in _processCleanup.GetProcessIdsStartedAfterBaseline(
                     _launchBaseline,
                     RunningApp?.ProcessNames ?? new HashSet<string>(),
                     _ownedModuleDirectory))
            _ownedProcessIds.Add(pid);

        return _ownedProcessIds.Where(IsProcessRunning).ToArray();
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch { return false; }
    }

    private void ShowLaunchError(string message)
    {
        CancelAndDispose(ref _errorCts);
        var cts = new CancellationTokenSource();
        _errorCts = cts;
        LaunchErrorMessage = message;
        IsLaunchErrorVisible = true;
        _ = HideLaunchErrorAfterDelay(cts);
    }

    private async Task HideLaunchErrorAfterDelay(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(LaunchErrorVisibleSeconds), cts.Token);
            IsLaunchErrorVisible = false;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_errorCts, cts))
                CancelAndDispose(ref _errorCts);
            else
                cts.Dispose(); // already superseded by a newer ShowLaunchError call
        }
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
