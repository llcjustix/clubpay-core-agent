using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core.Services;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Client.ViewModels;
using ClubPay.Agent.Client.Views;

namespace ClubPay.Agent.Client;

public partial class App : Application
{
    private ServiceProvider? _services;
    private CancellationTokenSource? _startupCts;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Installer mode — handles --install / --uninstall / --autologin args then exits
        if (HandleInstallerArgs(e.Args))
        {
            Shutdown(0);
            return;
        }

        // ── Normal kiosk startup ──────────────────────────────────────────────
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .Build();

        var sc = new ServiceCollection();

        sc.AddSingleton<IConfiguration>(config);

        // AgentStateRepository backs four interfaces from one local SQLite database — register it once
        // and forward all four to the same instance (separate AddSingleton<T,TImpl> calls would
        // otherwise create separate instances writing to the same file independently).
        sc.AddSingleton<AgentStateRepository>();
        sc.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<AgentStateRepository>());
        sc.AddSingleton<IGrantIdempotencyStore>(sp => sp.GetRequiredService<AgentStateRepository>());
        sc.AddSingleton<IControllerOutbox>(sp => sp.GetRequiredService<AgentStateRepository>());
        sc.AddSingleton<ICommandIdempotencyStore>(sp => sp.GetRequiredService<AgentStateRepository>());

        sc.AddSingleton<IAgentService, AgentService>();
        sc.AddSingleton<IKioskLockService, KioskLockService>();
        sc.AddSingleton<IProcessCleanupService, ProcessCleanupService>();
        sc.AddSingleton<IIdleDetectionService, IdleDetectionService>();
        sc.AddSingleton<ISystemClock, SystemClock>();
        sc.AddSingleton<ISessionCoordinator, SessionCoordinatorService>();
        sc.AddSingleton<IVoucherService, VoucherService>();
        sc.AddSingleton<IManagerCodeService, ManagerCodeService>();
        sc.AddSingleton<ILockCodeService, LockCodeService>();
        sc.AddSingleton<ICommandValidator, CommandValidationService>();
        sc.AddSingleton<ICommandDispatcher, CommandDispatcherService>();
        sc.AddSingleton<IControllerChannel, ControllerChannelService>();
        sc.AddSingleton<IConnectionStateProvider>(sp => sp.GetRequiredService<IControllerChannel>());
        sc.AddSingleton<QrCodeService>();
        sc.AddLogging(b => b.AddDebug().AddConsole());

        sc.AddSingleton<LockScreenViewModel>();
        sc.AddSingleton<ActiveSessionViewModel>();
        sc.AddSingleton<FreezeViewModel>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<GameLauncherViewModel>();

        sc.AddSingleton<KioskWindow>();
        sc.AddSingleton<SessionOverlayWindow>();
        sc.AddSingleton<GameLauncherWindow>();

        _services = sc.BuildServiceProvider();
        _startupCts = new CancellationTokenSource();

        _services.GetRequiredService<IKioskLockService>().Install();

        // Recover persisted session state (survives a crash/restart) before anything else touches it.
        await _services.GetRequiredService<AgentStateRepository>().LoadAsync(_startupCts.Token);
        await _services.GetRequiredService<ISessionCoordinator>().StartAsync(_startupCts.Token);

        // Wired here (not via constructor injection) to break the Dispatcher->Coordinator->Channel
        // ->Dispatcher construction cycle — see IControllerChannel.IncomingCommandHandler doc comment.
        // Must happen before StartAsync so the very first inbound command has somewhere to go.
        _services.GetRequiredService<IControllerChannel>().IncomingCommandHandler =
            _services.GetRequiredService<ICommandDispatcher>().DispatchAsync;
        await _services.GetRequiredService<IControllerChannel>().StartAsync(_startupCts.Token);

        _ = _services.GetRequiredService<SessionOverlayWindow>();
        _ = _services.GetRequiredService<GameLauncherWindow>();  // creates Instance
        _services.GetRequiredService<KioskWindow>().Show();

        _ = _services.GetRequiredService<MainViewModel>().InitializeAsync(_startupCts.Token);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _startupCts?.Cancel();
        if (_services is not null)
        {
            // Kill any running game before exit
            _services.GetRequiredService<GameLauncherViewModel>().KillRunningApp();
            await _services.GetRequiredService<IControllerChannel>().StopAsync();
            await _services.GetRequiredService<ISessionCoordinator>().DisposeAsync();
            _services.GetRequiredService<IKioskLockService>().Uninstall();
            _services.Dispose();
        }
        base.OnExit(e);
    }

    // ─── Installer argument handling ─────────────────────────────────────────

    private static bool HandleInstallerArgs(string[] args)
    {
        bool isInstall = args.Contains("--install");
        bool isUninstall = args.Contains("--uninstall");
        bool isStatus = args.Contains("--status");
        var loginArg = Array.Find(args, a => a.StartsWith("--autologin="));
        var noLoginArg = args.Contains("--disable-autologin");

        if (!isInstall && !isUninstall && !isStatus && loginArg is null && !noLoginArg)
            return false;

        // --status is read-only, no elevation needed
        if (isStatus)
        {
            var s = InstallService.GetStatus();
            MessageBox.Show(
                $"Fayllar o'rnatildi : {s.FilesInstalled}\n" +
                $"Startup ro'yxatda  : {s.StartupRegistered}\n" +
                $"Shell replacement  : {s.ShellReplaced}\n" +
                $"Auto-login yoqildi : {s.AutoLoginEnabled}\n" +
                $"Auto-login user    : {s.AutoLoginUser ?? "—"}",
                "ClubPay Agent — Holat",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }

        // Registry HKLM writes require elevation — re-launch with UAC if needed
        if (!InstallService.IsElevated)
        {
            InstallService.RelaunchElevated(args);
            return true;
        }

        if (isInstall)
        {
            bool shell = args.Contains("--shell");
            InstallService.Install(setShellReplacement: shell);
            var shellNote = shell ? "\nShell replacement yoqildi — faqat Agent ko'rinadi." : "";
            MessageBox.Show(
                $"O'rnatildi: {InstallService.InstallDir}\n" +
                $"Windows Startup'ga qo'shildi.{shellNote}\n\n" +
                "Endi kompyuter har yonganda Agent avtomatik ishga tushadi.",
                "ClubPay Agent — O'rnatildi",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        if (isUninstall)
        {
            var confirm = MessageBox.Show(
                "ClubPay Agent o'chirilsinmi?",
                "Tasdiqlash", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                InstallService.Uninstall();
                MessageBox.Show("O'chirildi. Kompyuterni qayta yuring.",
                    "ClubPay Agent", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        if (loginArg is not null)
        {
            // Format: --autologin=username:password  (password may be empty)
            var val = loginArg["--autologin=".Length..];
            var colon = val.IndexOf(':');
            var user = colon >= 0 ? val[..colon] : val;
            var pass = colon >= 0 ? val[(colon + 1)..] : "";
            InstallService.SetupAutoLogin(user, pass);
            MessageBox.Show(
                $"Auto-login sozlandi: {user}\n" +
                "Kompyuterni qayta yuring — login oynasisiz kiradi.",
                "ClubPay Agent", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        if (noLoginArg)
        {
            InstallService.DisableAutoLogin();
            MessageBox.Show("Auto-login o'chirildi.",
                "ClubPay Agent", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return true;
    }
}
