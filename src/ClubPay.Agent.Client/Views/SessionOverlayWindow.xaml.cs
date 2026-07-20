using System.Windows;

namespace ClubPay.Agent.Client.Views;

public partial class SessionOverlayWindow : Window
{
    public static SessionOverlayWindow? Instance { get; private set; }

    public SessionOverlayWindow(ViewModels.MainViewModel vm)
    {
        DataContext = vm;
        Instance = this;
        InitializeComponent();
        Loaded += (_, _) => PositionTopRight();
        // Width/Height now auto-size to content (small idle pill vs. larger warning cards+QR),
        // so re-anchor top-right whenever content size changes, not just once on load.
        SizeChanged += (_, _) => PositionTopRight();
    }

    public void ShowReturnButton() => ReturnBtn.Visibility = Visibility.Visible;
    public void HideReturnButton() => ReturnBtn.Visibility = Visibility.Collapsed;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Overlay never closes during a session — only hides (mirrors GameLauncherWindow;
        // this is a DI singleton whose Instance must stay usable for the app's lifetime)
        e.Cancel = true;
        Hide();
    }

    private void OnReturnClicked(object sender, RoutedEventArgs e)
        => GameLauncherWindow.Instance?.Vm.ReturnToLauncherCommand.Execute(null);

    private void PositionTopRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 20;
        Top  = area.Top  + 20;
    }
}
