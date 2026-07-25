using System.Windows;
using ClubPay.Agent.Client.ViewModels;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Views;

public partial class GameLauncherWindow : Window
{
    public static GameLauncherWindow? Instance { get; private set; }

    internal GameLauncherViewModel Vm => (GameLauncherViewModel)DataContext;

    public GameLauncherWindow(GameLauncherViewModel vm, IKioskLockService kioskLock)
    {
        Instance = this;
        DataContext = vm;
        InitializeComponent();

        // Fires on first launch AND when the already-running app's own tile is re-clicked
        // (GameLauncherViewModel.LaunchApp) — both cases mean "get the shell out of the way and
        // surface the running app", so both route through the same method.
        vm.AppLaunched += _ => Dispatcher.Invoke(HideShellForRunningApp);

        // Fires when the app fully exits, or the user explicitly clicked "return to launcher" —
        // both cases mean "show the tile grid".
        vm.ReturnRequested += () => Dispatcher.Invoke(() => ShowShell());

        // Ctrl+Shift+F9 while a game is running → peek at the shell (tile grid + full session
        // sidebar) without minimizing the game (ТЗ §22 "по горячей клавише, не мешает игре").
        kioskLock.ShellToggleRequested += () => Dispatcher.Invoke(ToggleShell);
    }

    /// <summary>Called by KioskWindow when the session becomes Active — including resuming from
    /// Frozen after a successful extend, where a game may already be running (KillRunningApp is
    /// never called on Frozen→Active, only on the transition to Locked). If a game is running we
    /// must NOT pop the tile-grid shell over it — ТЗ §7 requires resuming "с того же кадра", not
    /// interrupting play with the launcher. Only when nothing is running do we show the grid.</summary>
    public void ResumeForActiveSession()
    {
        if (Vm.IsAppRunning)
            HideShellForRunningApp();
        else
            ShowShell();
    }

    /// <summary>Called by KioskWindow when entering Locked or Frozen — hides the shell/return
    /// affordance entirely (no game should be reachable from here).</summary>
    public void EnterFullScreenOverlay()
    {
        Hide();
        SessionOverlayWindow.Instance?.HideReturnButton();
    }

    /// <summary>Shows the tile-grid shell. This window's own IsVisible is the single source of
    /// truth for "is the shell currently on screen" — no separate tracking flag (a prior version
    /// tracked hotkey-open state in a bool that could desync from the other show/hide paths,
    /// leaving the shell unreachable after a freeze or a "return to launcher" click).</summary>
    private void ShowShell(bool topmost = false)
    {
        Topmost = topmost;
        Show();
        Activate();
        SessionOverlayWindow.Instance?.HideReturnButton();
    }

    /// <summary>Hides the shell and brings whatever is running back to the foreground (real game
    /// window may not be the process we started directly — see GameLauncherViewModel.LaunchApp's
    /// Steam-handoff handling). Also re-arms the corner "return to launcher" button.</summary>
    private void HideShellForRunningApp()
    {
        Hide();
        Vm.BringRunningAppToForeground();
        SessionOverlayWindow.Instance?.ShowReturnButton();
    }

    private void ToggleShell()
    {
        if (!Vm.IsAppRunning)
            return; // no game running — the launcher is already the primary UI, nothing to toggle

        if (IsVisible)
            HideShellForRunningApp();
        else
            ShowShell(topmost: true);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Launcher never closes during a session — only hides
        e.Cancel = true;
        Hide();
    }
}
