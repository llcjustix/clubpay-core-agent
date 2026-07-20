using System.Windows;
using ClubPay.Agent.Client.ViewModels;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Views;

public partial class GameLauncherWindow : Window
{
    public static GameLauncherWindow? Instance { get; private set; }

    internal GameLauncherViewModel Vm => (GameLauncherViewModel)DataContext;

    // Tracks whether the shell is currently open because the hotkey summoned it mid-game — kept
    // separate from ordinary Show()/Hide() (e.g. at session start) so ToggleShell() knows whether
    // to open or close, and so a fresh game/return always starts from a known closed state.
    private bool _shellOpenViaHotkey;

    public GameLauncherWindow(GameLauncherViewModel vm, IKioskLockService kioskLock)
    {
        Instance    = this;
        DataContext = vm;
        InitializeComponent();

        // Game launched → hide launcher, show return button in overlay
        vm.AppLaunched += _ => Dispatcher.Invoke(() =>
        {
            Hide();
            _shellOpenViaHotkey = false;
            SessionOverlayWindow.Instance?.ShowReturnButton();
        });

        // Game exited or user clicked "return" → show launcher again
        vm.ReturnRequested += () => Dispatcher.Invoke(() =>
        {
            SessionOverlayWindow.Instance?.HideReturnButton();
            _shellOpenViaHotkey = false;
            Topmost = false;
            Show();
            Activate();
        });

        // Ctrl+Shift+F9 while a game is running → peek at the shell (tile grid + full session
        // sidebar) without minimizing the game (ТЗ §22 "по горячей клавише, не мешает игре").
        kioskLock.ShellToggleRequested += () => Dispatcher.Invoke(ToggleShell);
    }

    private void ToggleShell()
    {
        if (!Vm.IsAppRunning)
            return; // no game running — the launcher is already the primary UI, nothing to toggle

        if (_shellOpenViaHotkey)
        {
            Hide();
            Topmost = false;
        }
        else
        {
            Topmost = true;
            Show();
            Activate();
        }
        _shellOpenViaHotkey = !_shellOpenViaHotkey;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Launcher never closes during a session — only hides
        e.Cancel = true;
        Hide();
    }
}
