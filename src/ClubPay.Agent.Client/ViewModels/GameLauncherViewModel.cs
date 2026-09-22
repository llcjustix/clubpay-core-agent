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
    public ObservableCollection<LauncherCategoryGroup> CategoryGroups { get; } = [];
    // This is deliberately not a list of every Windows process.  The player can only
    // return to applications that were launched through ClubPay, so the kiosk shell
    // never turns into a gateway to Explorer or system tools.
    public ObservableCollection<LauncherApp> RunningApps { get; } = [];

    [ObservableProperty] private LauncherApp? _runningApp;
    [ObservableProperty] private bool         _isAppRunning;
    [ObservableProperty] private string?      _launchError;

    public event Action?             ReturnRequested;   // show launcher window
    // Raised only after a real top-level player window was found and restored.
    // Starting Steam.exe alone is not enough: on a VM its UI can appear seconds later.
    public event Action<LauncherApp> AppLaunched = delegate { };

    private readonly Dictionary<LauncherApp, Process?> _runningProcesses = [];
    private readonly HashSet<LauncherApp> _lifetimeMonitors = [];
    private readonly ILogger<GameLauncherViewModel> _logger;
    private readonly IConfiguration _config;
    private readonly SteamGameDiscoveryService _steamGames;
    private readonly LauncherCatalogService _catalog;
    private readonly LocalizationService _localizer;
    private readonly Dictionary<string, string> _catalogCategories = new(StringComparer.OrdinalIgnoreCase);

    public string ClubName { get; }
    public string PcId     { get; }

    public GameLauncherViewModel(
        IConfiguration config,
        SteamGameDiscoveryService steamGames,
        LauncherCatalogService catalog,
        LocalizationService localizer,
        ILogger<GameLauncherViewModel> logger)
    {
        _logger = logger;
        _config = config;
        _steamGames = steamGames;
        _catalog = catalog;
        _localizer = localizer;
        ClubName = config["Agent:ClubName"] ?? "ClubPay";
        PcId     = config["Agent:PcId"]     ?? "PC-01";

        _localizer.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == "Item[]")
                RefreshApps();
        };
        RefreshApps();
    }

    public void RefreshApps() => _ = RefreshAppsAsync();

    private async Task RefreshAppsAsync()
    {
        var discovered = new List<LauncherApp>();
        var section = _config.GetSection("Launcher:Apps");
        foreach (var item in section.GetChildren())
        {
            var name = item["Name"] ?? "?";
            var app = new LauncherApp(
                Name: name,
                ExePath: item["ExePath"] ?? "",
                Args: item["Args"] ?? "",
                IconPath: item["IconPath"] ?? "",
                Category: LauncherCategories.Normalize(item["Category"] ?? LauncherCategories.Classify(name)));
            if (IsPlayerLaunchable(app) && File.Exists(app.ExePath)) discovered.Add(app);
        }
        foreach (var app in _steamGames.Discover())
            if (!discovered.Any(existing => string.Equals(existing.Args, app.Args, StringComparison.OrdinalIgnoreCase) &&
                                            string.Equals(existing.ExePath, app.ExePath, StringComparison.OrdinalIgnoreCase)))
                discovered.Add(app);

        // Keep the last confirmed manager choices while the next background request runs.
        // Otherwise every 30-second poll briefly replaces a manager category with its
        // automatic fallback before the response arrives.
        ReplaceApps(ApplyCatalogCategories(discovered));
        try
        {
            var externalPcId = MachineNameTemplate.Expand(_config["Controller:ExternalPcId"]);
            var categories = await _catalog.SyncAsync(externalPcId ?? PcId.ToLowerInvariant(), discovered);
            if (categories.Count == 0) return;

            _catalogCategories.Clear();
            foreach (var (key, category) in categories)
                _catalogCategories[key] = category;
            ReplaceApps(ApplyCatalogCategories(discovered));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Launcher category synchronization failed");
        }
    }

    private IEnumerable<LauncherApp> ApplyCatalogCategories(IEnumerable<LauncherApp> apps) =>
        apps.Select(app => _catalogCategories.TryGetValue(app.Key, out var category)
            ? app with { Category = category }
            : app);

    private void ReplaceApps(IEnumerable<LauncherApp> apps)
    {
        var localized = apps
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(app => app with { Category = LocalizeCategory(app.Category) })
            .ToList();

        // The catalog is refreshed in the background. Do not clear and rebuild the
        // visible launcher when neither discovery nor a manager-selected category changed.
        if (Apps.Count == localized.Count && Apps.SequenceEqual(localized))
            return;

        Apps.Clear();
        foreach (var app in localized)
            Apps.Add(app);

        CategoryGroups.Clear();
        foreach (var group in localized
            .GroupBy(app => app.Category, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => CategoryOrder(group.Key)))
        {
            CategoryGroups.Add(new LauncherCategoryGroup(group.Key, group.ToList()));
        }
    }

    private string LocalizeCategory(string? category) => LauncherCategories.Normalize(category) switch
    {
        LauncherCategories.Shooter => _localizer["Shooter"],
        LauncherCategories.Strategy => _localizer["Strategy"],
        _ => _localizer["Other"],
    };

    private static int CategoryOrder(string category) => category switch
    {
        "Шутеры" or "Otishmalar" => 0,
        "Стратегии" or "Strategiyalar" => 1,
        _ => 2,
    };

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
            LaunchError = _localizer.Format("LaunchFailed", app.Name);
            return;
        }

        _ = PromoteAppWhenReadyAsync(app);
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
        var target = app ?? RunningApp ?? RunningApps.LastOrDefault();
        if (target is null || !RunningApps.Contains(target))
            return false;

        // Steam is noticeably slower on the pilot VM, especially on its first run.
        // Keep the launcher visible during this wait rather than exposing a blank
        // kiosk background just because Steam has started its process tree.
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (TryFocusRunningApp(target))
            {
                EnsureLifetimeMonitor(target);
                AppLaunched(target);
                return true;
            }

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

                // Process.Start may return a short-lived bootstrapper. Track the
                // process that actually owns the visible player window instead,
                // so closing that application removes its dock entry.
                _runningProcesses[app] = process;
                RunningApp = app;
                // Let the fullscreen Agent drop behind the player window before
                // requesting foreground. Doing this afterwards lets the WPF
                // launcher win the z-order race on slower Steam startups.
                AppLaunched(app);
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

    private async Task PromoteAppWhenReadyAsync(LauncherApp app)
    {
        // Do not hide the launcher until the player application actually owns a
        // visible window. This is important for Steam, whose bootstrap process is
        // often alive well before the desktop client is ready.
        if (await FocusRunningAppAsync(app))
            return;

        // No visible window appeared during the startup grace period. Keep the
        // launcher on screen and remove the stale taskbar item instead of leaving
        // the player with a dock entry that cannot be opened.
        if (RunningApps.Contains(app))
        {
            UntrackApp(app);
            LaunchError = _localizer.Format("LaunchFailed", app.Name);
        }
    }

    private void EnsureLifetimeMonitor(LauncherApp app)
    {
        if (!_lifetimeMonitors.Add(app))
            return;

        _ = MonitorAppLifetimeAsync(app);
    }

    private async Task MonitorAppLifetimeAsync(LauncherApp app)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));

            if (IsTrackedAppStillRunning(app))
                continue;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _lifetimeMonitors.Remove(app);
                if (!RunningApps.Contains(app))
                    return;

                UntrackApp(app);
                if (!IsAppRunning)
                    ReturnRequested?.Invoke();
            });
            return;
        }
    }

    private bool IsTrackedAppStillRunning(LauncherApp app)
    {
        if (!_runningProcesses.TryGetValue(app, out var process) || process is null)
            return false;

        try
        {
            process.Refresh();
            // A player-facing app is open only while its window is visible. Steam
            // commonly keeps steam.exe alive in the tray after the player closes its
            // window; keeping that stale process in the dock is misleading.
            return !process.HasExited && NativeLauncher.IsVisibleWindow(process.MainWindowHandle);
        }
        catch
        {
            return false;
        }
    }

    private IEnumerable<Process> GetTrackedProcesses(LauncherApp app)
    {
        var candidates = new List<Process>();
        if (_runningProcesses.TryGetValue(app, out var launchedProcess) && launchedProcess is not null)
            candidates.Add(launchedProcess);

        foreach (var processName in RelatedProcessNames(app))
        {
            try { candidates.AddRange(Process.GetProcessesByName(processName)); }
            catch (Exception ex) { _logger.LogDebug(ex, "Could not find process {ProcessName} for {AppName}", processName, app.Name); }
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
        _lifetimeMonitors.Clear();
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
        _lifetimeMonitors.Remove(app);
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

        try
        {
            foreach (var processName in RelatedProcessNames(app))
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

    internal static IReadOnlyList<string> RelatedProcessNames(LauncherApp app)
    {
        var processName = Path.GetFileNameWithoutExtension(app.ExePath);
        if (string.IsNullOrWhiteSpace(processName))
            return [];

        // The current Steam desktop client owns its visible window through a
        // steamwebhelper.exe process. Looking only at Steam.exe gives a successful
        // launch state with no HWND to restore, leaving the opaque Agent shell on top.
        return IsSteamLaunch(app)
            ? ["steam", "steamwebhelper"]
            : [processName];
    }

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
    private static readonly nint HwndTop = nint.Zero;
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    internal static bool IsVisibleWindow(nint hWnd) =>
        hWnd != nint.Zero && IsWindow(hWnd) && IsWindowVisible(hWnd);

    public static void RestoreAndForeground(nint hWnd)
    {
        // This is called from a player's explicit click, which lets Windows accept
        // the foreground request without exposing Explorer/taskbar in kiosk mode.
        ShowWindowAsync(hWnd, SW_RESTORE);
        // Keep the ClubPay dock above the player application. HWND_TOP restores
        // Steam among normal windows; HWND_TOPMOST would cover the dock again.
        SetWindowPos(hWnd, HwndTop, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpShowWindow);
        BringWindowToTop(hWnd);
        SetForegroundWindow(hWnd);
    }
}


public sealed class LauncherCategoryGroup
{
    public LauncherCategoryGroup(string name, IReadOnlyList<LauncherApp> apps)
    {
        Name = name;
        Apps = apps;
    }

    public string Name { get; }
    public IReadOnlyList<LauncherApp> Apps { get; }
}
