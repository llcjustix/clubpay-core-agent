using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.ViewModels;

public partial class GameLauncherViewModel : ObservableObject
{
    public ObservableCollection<LauncherApp> Apps { get; } = [];
    // This is deliberately not a list of every Windows process.  The player can only
    // return to applications that were launched through ClubPay, so the kiosk shell
    // never turns into a gateway to Explorer or system tools.
    public ObservableCollection<LauncherApp> RunningApps { get; } = [];

    [ObservableProperty] private LauncherApp? _runningApp;
    [ObservableProperty] private bool         _isAppRunning;
    [ObservableProperty] private string?      _launchError;

    public event Action?             ReturnRequested;   // show launcher window
    public event Action<LauncherApp> AppLaunched = delegate { }; // external game takes foreground

    private readonly Dictionary<LauncherApp, Process?> _runningProcesses = [];
    private readonly ILogger<GameLauncherViewModel> _logger;

    public string ClubName { get; }
    public string PcId     { get; }

    public GameLauncherViewModel(
        IConfiguration config,
        SteamGameDiscoveryService steamGames,
        ILogger<GameLauncherViewModel> logger)
    {
        _logger = logger;
        ClubName = config["Agent:ClubName"] ?? "NEXUS ARENA";
        PcId     = config["Agent:PcId"]     ?? "PC-01";

        var section = config.GetSection("Launcher:Apps");
        foreach (var item in section.GetChildren())
        {
            var app = new LauncherApp(
                Name:     item["Name"]     ?? "?",
                ExePath:  item["ExePath"]  ?? "",
                Args:     item["Args"]     ?? "",
                IconPath: item["IconPath"] ?? "",
                Category: item["Category"] ?? "O'yin");
            // Never show an attractive but non-working tile: configured apps must exist locally.
            if (IsPlayerLaunchable(app) && File.Exists(app.ExePath))
                Apps.Add(app);
        }

        foreach (var app in steamGames.Discover())
        {
            if (!Apps.Any(existing => string.Equals(existing.Args, app.Args, StringComparison.OrdinalIgnoreCase) &&
                                      string.Equals(existing.ExePath, app.ExePath, StringComparison.OrdinalIgnoreCase)))
                Apps.Add(app);
        }
    }

    [RelayCommand]
    public async Task LaunchApp(LauncherApp app)
    {
        // A second click on an already-open tile is the same as clicking its taskbar
        // button: restore the existing application instead of launching a duplicate.
        if (RunningApps.Contains(app))
        {
            if (await FocusRunningAppAsync(app))
                return;

            // The process/window disappeared outside of our control. Make this click
            // a fresh launch and remove only this stale taskbar item.
            UntrackApp(app);
        }

        LaunchError = null;

        var isSteamGame = IsSteamGameLaunch(app);
        var psi = isSteamGame
            ? new ProcessStartInfo
            {
                // The URI is handled by the installed Steam client. It is more reliable than
                // starting Steam.exe directly, particularly when Steam is already running.
                FileName = $"steam://run/{GetSteamAppId(app)}",
                UseShellExecute = true
            }
            : new ProcessStartInfo
            {
                FileName = app.ExePath,
                Arguments = app.Args,
                UseShellExecute = true
            };

        try
        {
            var process = Process.Start(psi);
            TrackApp(app, process);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not launch {AppName}", app.Name);
            LaunchError = $"Не удалось запустить «{app.Name}». Проверьте, что Steam установлен и пользователь вошёл в аккаунт.";
            return;
        }

        AppLaunched(app);  // retain Agent as fullscreen background, then let game take foreground
        _ = PromoteAppWhenReadyAsync(app);

        // Both a Steam game URI and Steam.exe may hand control to an already-running Steam client
        // and exit immediately. That hand-off is not the end of the player's application: keep
        // Agent in external-app mode until the player explicitly returns or the session ends.
        if (IsSteamLaunch(app))
            return;

        if (_runningProcesses.TryGetValue(app, out var launchedProcess) && launchedProcess is not null)
        {
            await launchedProcess.WaitForExitAsync();
        }

        UntrackApp(app);
        if (!IsAppRunning)
            ReturnRequested?.Invoke(); // last game exited → show launcher again
    }

    [RelayCommand]
    public void ReturnToLauncher()
    {
        // This is a real minimise, not an application close.  The entry remains in the
        // ClubPay dock and can be restored with one click, like a Windows taskbar item.
        if (TryGetWindowHandle(RunningApp, out var hwnd))
            NativeLauncher.ShowWindow(hwnd, NativeLauncher.SW_MINIMIZE);

        ReturnRequested?.Invoke();
    }

    [RelayCommand]
    public async Task<bool> FocusRunningAppAsync(LauncherApp? app)
    {
        // Steam and games commonly create their main window a moment after their launcher
        // process starts. Retry before deciding the dock item is stale.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (TryFocusRunningApp(app))
                return true;

            await Task.Delay(250);
        }

        return false;
    }

    [RelayCommand]
    public async Task CloseRunningApp(LauncherApp? app)
    {
        app ??= RunningApp;
        if (app is null || !RunningApps.Contains(app))
            return;

        // A dock entry is created only for an app launched by ClubPay. Closing it
        // must therefore affect only that player's app, never Explorer or arbitrary
        // Windows processes. Try a graceful close first, then force-close only the
        // still-running process after a short grace period.
        var processes = GetTrackedProcesses(app).ToList();
        foreach (var process in processes)
        {
            try
            {
                process.Refresh();
                if (!process.HasExited && process.MainWindowHandle != nint.Zero)
                    process.CloseMainWindow();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not request close for {AppName}", app.Name);
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(2));

        foreach (var process in processes)
        {
            try
            {
                process.Refresh();
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not terminate {AppName}", app.Name);
            }
            finally
            {
                process.Dispose();
            }
        }

        UntrackApp(app);
        if (!IsAppRunning)
            ReturnRequested?.Invoke();
    }

    [RelayCommand]
    public void ShowLauncher()
        => ReturnRequested?.Invoke();

    private bool TryFocusRunningApp(LauncherApp? app)
    {
        app ??= RunningApp ?? RunningApps.LastOrDefault();
        if (app is null || !RunningApps.Contains(app))
            return false;

        // Process.Start("steam://…") commonly exits immediately after handing work to
        // the real Steam process.  Search by executable name as well as the process we
        // originally started, so both Steam itself and installed Steam games can be
        // recovered from the ClubPay UI after being minimised or covered.
        var candidates = GetTrackedProcesses(app);

        var visitedProcessIds = new HashSet<int>();
        foreach (var process in candidates)
        {
            try
            {
                if (!visitedProcessIds.Add(process.Id))
                    continue;

                process.Refresh();
                if (process.HasExited || process.MainWindowHandle == nint.Zero)
                    continue;

                NativeLauncher.RestoreAndForeground(process.MainWindowHandle);
                RunningApp = app;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not foreground {AppName}", app.Name);
            }
        }

        return false;
    }

    private async Task PromoteAppWhenReadyAsync(LauncherApp app)
    {
        // The Agent launcher is intentionally topmost to hide Explorer and any stray
        // administrative windows. Promote only the player app above that layer once its
        // native window exists; the timer therefore never covers Steam or a game.
        await FocusRunningAppAsync(app);
    }

    private IEnumerable<Process> GetTrackedProcesses(LauncherApp app)
    {
        var candidates = new List<Process>();
        if (_runningProcesses.TryGetValue(app, out var launchedProcess) && launchedProcess is not null)
            candidates.Add(launchedProcess);

        var processName = Path.GetFileNameWithoutExtension(app.ExePath);
        if (!string.IsNullOrWhiteSpace(processName))
        {
            try { candidates.AddRange(Process.GetProcessesByName(processName)); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not find running process for {AppName}", app.Name); }
        }

        return candidates
            .GroupBy(process => process.Id)
            .Select(group => group.First());
    }

    public void KillRunningApp()
    {
        foreach (var process in _runningProcesses.Values.Where(process => process is not null))
        {
            try { process!.Kill(entireProcessTree: true); }
            catch { }
        }

        _runningProcesses.Clear();
        RunningApps.Clear();
        RunningApp   = null;
        IsAppRunning = false;
    }

    private void TrackApp(LauncherApp app, Process? process)
    {
        _runningProcesses[app] = process;
        if (!RunningApps.Contains(app))
            RunningApps.Add(app);

        RunningApp = app;
        IsAppRunning = true;
    }

    private void UntrackApp(LauncherApp app)
    {
        _runningProcesses.Remove(app);
        RunningApps.Remove(app);
        RunningApp = RunningApps.LastOrDefault();
        IsAppRunning = RunningApps.Count > 0;
    }

    private bool TryGetWindowHandle(LauncherApp? app, out nint handle)
    {
        handle = nint.Zero;
        if (app is null)
            return false;

        if (_runningProcesses.TryGetValue(app, out var process) && process is not null)
        {
            try
            {
                process.Refresh();
                if (!process.HasExited && process.MainWindowHandle != nint.Zero)
                {
                    handle = process.MainWindowHandle;
                    return true;
                }
            }
            catch { }
        }

        var processName = Path.GetFileNameWithoutExtension(app.ExePath);
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        try
        {
            foreach (var candidate in Process.GetProcessesByName(processName))
            {
                using (candidate)
                {
                    if (candidate.MainWindowHandle == nint.Zero)
                        continue;

                    handle = candidate.MainWindowHandle;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not find window handle for {AppName}", app.Name);
        }

        return false;
    }

    private static bool IsSteamGameLaunch(LauncherApp app) =>
        Path.GetFileName(app.ExePath).Equals("steam.exe", StringComparison.OrdinalIgnoreCase) &&
        app.Args.StartsWith("-applaunch ", StringComparison.OrdinalIgnoreCase);

    private static bool IsSteamLaunch(LauncherApp app) =>
        Path.GetFileName(app.ExePath).Equals("steam.exe", StringComparison.OrdinalIgnoreCase);

    private static string GetSteamAppId(LauncherApp app) =>
        app.Args["-applaunch ".Length..].Trim();

    private static bool IsPlayerLaunchable(LauncherApp app) =>
        // Steam itself is deliberately shown so a player can sign in. Its common redistributables
        // package is merely a runtime dependency, never a player-facing application.
        !app.Name.Equals("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase);
}

internal static class NativeLauncher
{
    public const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpShowWindow = 0x0040;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    public static void RestoreAndForeground(nint hWnd)
    {
        // This is called from a player's explicit click, which lets Windows accept
        // the foreground request without exposing Explorer/taskbar in kiosk mode.
        ShowWindowAsync(hWnd, SW_RESTORE);
        SetWindowPos(hWnd, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpShowWindow);
        BringWindowToTop(hWnd);
        SetForegroundWindow(hWnd);
    }
}
