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
    }

    public void ShowReturnButton() => ReturnBtn.Visibility = Visibility.Visible;
    public void HideReturnButton() => ReturnBtn.Visibility = Visibility.Collapsed;

    private void OnReturnClicked(object sender, RoutedEventArgs e)
        => GameLauncherWindow.Instance?.Vm.ReturnToLauncherCommand.Execute(null);

    private void PositionTopRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 20;
        Top  = area.Top  + 20;
    }
}
