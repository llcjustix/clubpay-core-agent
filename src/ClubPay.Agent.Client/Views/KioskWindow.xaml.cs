using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using ClubPay.Agent.Client.ViewModels;

namespace ClubPay.Agent.Client.Views;

public partial class KioskWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;
    private readonly bool _maintenanceExitEnabled;

    public KioskWindow(MainViewModel vm, IConfiguration configuration)
    {
        DataContext = vm;
        _maintenanceExitEnabled = configuration.GetValue("Agent:MaintenanceExitEnabled", false);
        InitializeComponent();

        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsActive))
                UpdateVisibility();
        };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Development / maintenance only. Production configs keep this disabled,
        // so a customer cannot close the kiosk using this shortcut.
        if (_maintenanceExitEnabled && e.Key == Key.F12 &&
            Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            Application.Current.Shutdown();
            return;
        }

        // Lets an administrator access a local Controller on the same PC during
        // commissioning without terminating the Agent or dropping its WebSocket.
        // A session state change restores the customer UI automatically.
        if (_maintenanceExitEnabled && e.Key == Key.F11 &&
            Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            Topmost = false;
            WindowState = WindowState.Minimized;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => FullScreenWindow.CoverPrimaryScreen(this);

    private void UpdateVisibility()
    {
        if (Vm.IsActive)
        {
            // The launcher is the active-session shell. Keeping this full-screen
            // KioskWindow visible below it works only while the launcher is topmost;
            // once a player app opens, the launcher becomes a normal window and the
            // empty kiosk background would cover it. Render the launcher first, then
            // hide this window completely for the active-session lifetime.
            Topmost = false;
            GameLauncherWindow.Instance?.ShowLauncherSurface();
            PlayerDockWindow.Instance?.ShowDock();
            Dispatcher.BeginInvoke(() =>
            {
                if (Vm.IsActive && GameLauncherWindow.Instance?.IsVisible == true)
                    Hide();
            }, DispatcherPriority.Render);
        }
        else
        {
            GameLauncherWindow.Instance?.EnterLauncherMode();
            GameLauncherWindow.Instance?.Hide();
            PlayerDockWindow.Instance?.Hide();
            Topmost = true;
            Show();
            Activate();
            Focus();
        }
    }
}
