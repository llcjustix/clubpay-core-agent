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

    private void UpdateVisibility()
    {
        if (Vm.IsActive)
        {
            Hide();
            SessionOverlayWindow.Instance?.Show();
            GameLauncherWindow.Instance?.Show();
            GameLauncherWindow.Instance?.Activate();
        }
        else
        {
            GameLauncherWindow.Instance?.Hide();
            SessionOverlayWindow.Instance?.Hide();
            SessionOverlayWindow.Instance?.HideReturnButton();
            Show();
            Activate();
            Focus();
        }
    }
}
