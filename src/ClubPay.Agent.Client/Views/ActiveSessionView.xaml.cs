using System.Windows;
using System.Windows.Controls;

namespace ClubPay.Agent.Client.Views;

public partial class ActiveSessionView : UserControl
{
    public event Func<Task>? EndSessionRequested;

    public ActiveSessionView() => InitializeComponent();

    private void OnMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.ContextMenu is not { } menu)
            return;

        menu.PlacementTarget = control;
        menu.IsOpen = true;
    }

    private async void OnEndSessionClicked(object sender, RoutedEventArgs e)
    {
        if (EndSessionRequested is null)
            return;

        foreach (var handler in EndSessionRequested.GetInvocationList().Cast<Func<Task>>())
            await handler();
    }
}
