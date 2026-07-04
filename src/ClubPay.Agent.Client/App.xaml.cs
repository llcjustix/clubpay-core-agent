using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core.Models;
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
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var sc = new ServiceCollection();

        sc.AddSingleton<IConfiguration>(config);
        sc.AddSingleton<ISessionStore,       SessionStore>();
        sc.AddSingleton<IAgentService,       AgentService>();
        sc.AddSingleton<IVoucherService,     StubVoucherService>();
        sc.AddSingleton<IKioskLockService,      KioskLockService>();
        sc.AddSingleton<IControllerListener,       ControllerListenerService>();
        sc.AddSingleton<ICoreApiListener,          CoreApiListenerService>();
        sc.AddSingleton<IBillingEventReporter,     BillingEventReporterService>();
        sc.AddSingleton<IProcessCleanupService,    ProcessCleanupService>();
        sc.AddSingleton<IStartupSessionChecker,    StartupSessionCheckerService>();
        sc.AddSingleton<QrCodeService>();
        sc.AddLogging(b => b.AddDebug());

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
        await _services.GetRequiredService<IControllerListener>().StartAsync(_startupCts.Token);
        await _services.GetRequiredService<ICoreApiListener>().StartAsync(_startupCts.Token);

        _ = _services.GetRequiredService<SessionOverlayWindow>();
        _ = _services.GetRequiredService<GameLauncherWindow>();  // creates Instance
        _services.GetRequiredService<KioskWindow>().Show();

        // Locked ekran ko'rsatilgandan keyin pending sessiya tekshiriladi
        _ = _services.GetRequiredService<MainViewModel>().InitializeAsync(_startupCts.Token);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _startupCts?.Cancel();
        if (_services is not null)
        {
            // Kill any running game before exit
            _services.GetRequiredService<GameLauncherViewModel>().KillRunningApp();
            await _services.GetRequiredService<IControllerListener>().StopAsync();
            await _services.GetRequiredService<ICoreApiListener>().StopAsync();
            _services.GetRequiredService<IKioskLockService>().Uninstall();
            _services.Dispose();
        }
        base.OnExit(e);
    }

    // ─── Installer argument handling ─────────────────────────────────────────

    private static bool HandleInstallerArgs(string[] args)
    {
        bool isInstall   = args.Contains("--install");
        bool isUninstall = args.Contains("--uninstall");
        bool isStatus    = args.Contains("--status");
        var  loginArg    = Array.Find(args, a => a.StartsWith("--autologin="));
        var  noLoginArg  = args.Contains("--disable-autologin");

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
            var val   = loginArg["--autologin=".Length..];
            var colon = val.IndexOf(':');
            var user  = colon >= 0 ? val[..colon]       : val;
            var pass  = colon >= 0 ? val[(colon + 1)..] : "";
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

// Stub voucher service — replace with Ed25519 implementation
file sealed class StubVoucherService : IVoucherService
{
    public VoucherToken? Redeem(string code, string pcId) => null;
    public bool Validate(VoucherToken token, string pcId, DateTime nowUtc) => false;
}
