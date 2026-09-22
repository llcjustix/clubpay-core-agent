using System.Windows;
using System.Windows.Controls;

namespace ClubPay.Agent.Client.Views;

public partial class ActiveSessionView : UserControl
{
    public event Func<Task>? EndSessionRequested;

    public ActiveSessionView()
    {
        InitializeComponent();
    }

    private void OnMenuClicked(object sender, RoutedEventArgs e)
    {
        SessionActionMenu.Visibility = SessionActionMenu.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        // Reassert the dock after any player control is used. Its own topmost
        // HWND can otherwise fall behind the fullscreen launcher after RDP has
        // processed a popup/focus transition.
        PlayerDockWindow.Instance?.ShowDock();
    }

    private async void OnEndSessionClicked(object sender, RoutedEventArgs e)
    {
        SessionActionMenu.Visibility = Visibility.Collapsed;
        if (EndSessionRequested is null)
            return;

        foreach (var handler in EndSessionRequested.GetInvocationList().Cast<Func<Task>>())
            await handler();
    }
}
