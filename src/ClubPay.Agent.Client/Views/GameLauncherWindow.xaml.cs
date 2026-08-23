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

        // A game is an external Windows process, but the ClubPay launcher must stay visible
        // behind it. Hiding the launcher here exposed Explorer/the normal desktop whenever a
        // game ran windowed or switched screens. The game receives foreground normally; Agent
        // remains its fullscreen protected background.
        vm.AppLaunched += _ => Dispatcher.Invoke(() =>
        {
            if (!IsVisible)
                Show();
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
