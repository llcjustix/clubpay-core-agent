using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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
        if (sender is not Control control)
            return;

        // A WPF ContextMenu can only have one logical parent.  Do not keep a
        // shared instance in XAML or reuse a previously attached menu.
        var menu = new ContextMenu
        {
            PlacementTarget = control
        };

        var endSession = new MenuItem();
        endSession.SetBinding(
            MenuItem.HeaderProperty,
            new Binding("[EndSession]")
            {
                Source = Application.Current?.TryFindResource("Loc"),
                Mode = BindingMode.OneWay
            });
        endSession.Click += OnEndSessionClicked;
        menu.Items.Add(endSession);

        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(control.ContextMenu, menu))
                control.ContextMenu = null;
        };

        control.ContextMenu = menu;
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
