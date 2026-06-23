using System.Windows;
using ClubPay.Agent.Client.ViewModels;

namespace ClubPay.Agent.Client.Views;

public partial class KioskWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public KioskWindow(MainViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();

        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsActive))
                UpdateVisibility();
        };
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
