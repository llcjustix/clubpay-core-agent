using System.Windows;
using System.Windows.Input;
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

    /// <summary>ТЗ §7: "Нажмите Enter, чтобы ввести код" — Enter reveals the LockScreen code input,
    /// Escape hides it. Tunneling (Preview) so it works no matter what has keyboard focus; once the
    /// input is visible, Enter falls through to the TextBox's own Return binding that submits.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Vm.IsLocked)
        {
            if (e.Key == Key.Enter && !Vm.LockScreen.IsInputVisible)
            {
                Vm.LockScreen.RevealInputCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && Vm.LockScreen.IsInputVisible)
            {
                Vm.LockScreen.HideInputCommand.Execute(null);
                e.Handled = true;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    private void UpdateVisibility()
    {
        if (Vm.IsActive)
        {
            Hide();
            GameLauncherWindow.Instance?.ResumeForActiveSession();
        }
        else
        {
            GameLauncherWindow.Instance?.EnterFullScreenOverlay();
            SessionOverlayWindow.Instance?.Hide();
            Show();
            Activate();
            Focus();
        }
    }
}
