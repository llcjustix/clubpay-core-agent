using System.Windows;
using ClubPay.Agent.Client.ViewModels;

namespace ClubPay.Agent.Client.Views;

public partial class GameLauncherWindow : Window
{
    public static GameLauncherWindow? Instance { get; private set; }

    internal GameLauncherViewModel Vm => (GameLauncherViewModel)DataContext;

    public GameLauncherWindow(GameLauncherViewModel vm)
    {
        Instance    = this;
        DataContext = vm;
        InitializeComponent();

        // Game launched → hide launcher, show return button in overlay
        vm.AppLaunched += _ => Dispatcher.Invoke(() =>
        {
            Hide();
            SessionOverlayWindow.Instance?.ShowReturnButton();
        });

        // Game exited or user clicked "return" → show launcher again
        vm.ReturnRequested += () => Dispatcher.Invoke(() =>
        {
            SessionOverlayWindow.Instance?.HideReturnButton();
            Show();
            Activate();
        });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Launcher never closes during a session — only hides
        e.Cancel = true;
        Hide();
    }
}
