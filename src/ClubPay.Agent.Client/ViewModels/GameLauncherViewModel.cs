using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.ViewModels;

public partial class GameLauncherViewModel : ObservableObject
{
    public ObservableCollection<LauncherApp> Apps { get; } = [];

    [ObservableProperty] private LauncherApp? _runningApp;
    [ObservableProperty] private bool         _isAppRunning;

    public event Action?             ReturnRequested;   // show launcher window
    public event Action<LauncherApp> AppLaunched = delegate { }; // hide launcher, game visible

    private Process? _currentProcess;

    public string ClubName { get; }
    public string PcId     { get; }

    public GameLauncherViewModel(IConfiguration config, SteamGameDiscoveryService steamGames)
    {
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
            if (!string.IsNullOrEmpty(app.ExePath) && File.Exists(app.ExePath))
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
        if (IsAppRunning) return;

        var psi = new ProcessStartInfo
        {
            FileName        = app.ExePath,
            Arguments       = app.Args,
            UseShellExecute = true
        };

        try
        {
            _currentProcess = Process.Start(psi);
        }
        catch { return; }

        RunningApp    = app;
        IsAppRunning  = true;
        AppLaunched(app);  // signal to hide launcher window

        // Steam hands a launch request to its client and the Steam.exe process may return straight
        // away. Keep the launcher hidden until the player explicitly returns; session cleanup still
        // closes every process spawned after the session baseline.
        if (IsSteamGameLaunch(app))
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

        ReturnRequested?.Invoke();
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
}

internal static class NativeLauncher
{
    public const int SW_MINIMIZE = 6;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);
}
