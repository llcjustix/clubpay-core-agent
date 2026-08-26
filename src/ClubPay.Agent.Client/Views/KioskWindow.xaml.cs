using System.Windows;
using System.Windows.Input;
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

        base.OnPreviewKeyDown(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => FullScreenWindow.CoverPrimaryScreen(this);

    private void UpdateVisibility()
    {
        if (Vm.IsActive)
        {
            // Do not hide the only full-screen Agent surface before the launcher is
            // ready. Hiding it first leaves a brief frame where Explorer/Windows is
            // visible during a successful payment → session transition.
            //
            // Keep the kiosk as the opaque background, but drop its topmost flag so
            // the player launcher can sit above it. It is restored to topmost as
            // soon as the session ends.
            Topmost = false;
            GameLauncherWindow.Instance?.ShowLauncherSurface();
            PlayerDockWindow.Instance?.ShowDock();
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
