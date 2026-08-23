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

    [ObservableProperty] private LauncherApp? _runningApp;
    [ObservableProperty] private bool         _isAppRunning;
    [ObservableProperty] private string?      _launchError;

    public event Action?             ReturnRequested;   // show launcher window
    public event Action<LauncherApp> AppLaunched = delegate { }; // external game takes foreground

    private Process? _currentProcess;
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
        // A player can minimise Steam (or click the launcher background) at any point.
        // Do not turn the tile into a dead button in that state: a second click must
        // restore the already-running player application instead of starting a copy.
        if (IsAppRunning)
        {
            if (TryFocusRunningApp())
                return;

            // The process/window disappeared outside of our control.  Clear the stale
            // state and make this click a normal fresh launch.
            _currentProcess = null;
            RunningApp = null;
            IsAppRunning = false;
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
            _currentProcess = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not launch {AppName}", app.Name);
            LaunchError = $"Не удалось запустить «{app.Name}». Проверьте, что Steam установлен и пользователь вошёл в аккаунт.";
            return;
        }

        RunningApp    = app;
        IsAppRunning  = true;
        AppLaunched(app);  // retain Agent as fullscreen background, then let game take foreground

        // Both a Steam game URI and Steam.exe may hand control to an already-running Steam client
        // and exit immediately. That hand-off is not the end of the player's application: keep
        // Agent in external-app mode until the player explicitly returns or the session ends.
        if (IsSteamLaunch(app))
            return;

        if (_currentProcess is not null)
        {
            await _currentProcess.WaitForExitAsync();
            _currentProcess = null;
        }

        RunningApp   = null;
        IsAppRunning = false;
        ReturnRequested?.Invoke(); // game exited → show launcher again
    }

    [RelayCommand]
    public void ReturnToLauncher()
    {
        // Minimise running game so launcher appears in front
        if (_currentProcess is { HasExited: false, MainWindowHandle: var hwnd } && hwnd != nint.Zero)
            NativeLauncher.ShowWindow(hwnd, NativeLauncher.SW_MINIMIZE);

        _currentProcess = null;
        RunningApp = null;
        IsAppRunning = false;
        ReturnRequested?.Invoke();
    }

    [RelayCommand]
    public void FocusRunningApp()
        => TryFocusRunningApp();

    private bool TryFocusRunningApp()
    {
        var app = RunningApp;
        if (app is null)
            return false;

        // Process.Start("steam://…") commonly exits immediately after handing work to
        // the real Steam process.  Search by executable name as well as the process we
        // originally started, so both Steam itself and installed Steam games can be
        // recovered from the ClubPay UI after being minimised or covered.
        var candidates = new List<Process>();
        if (_currentProcess is not null)
            candidates.Add(_currentProcess);

        var processName = Path.GetFileNameWithoutExtension(app.ExePath);
        if (!string.IsNullOrWhiteSpace(processName))
        {
            try { candidates.AddRange(Process.GetProcessesByName(processName)); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not find running process for {AppName}", app.Name); }
        }

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
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not foreground {AppName}", app.Name);
            }
        }

        return false;
    }

    public void KillRunningApp()
    {
        try { _currentProcess?.Kill(entireProcessTree: true); }
        catch { }
        _currentProcess = null;
        RunningApp   = null;
        IsAppRunning = false;
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    public static void RestoreAndForeground(nint hWnd)
    {
        // This is called from a player's explicit click, which lets Windows accept
        // the foreground request without exposing Explorer/taskbar in kiosk mode.
        ShowWindowAsync(hWnd, SW_RESTORE);
        BringWindowToTop(hWnd);
        SetForegroundWindow(hWnd);
    }
}
