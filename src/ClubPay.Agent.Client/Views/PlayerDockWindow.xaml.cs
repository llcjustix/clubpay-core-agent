using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ClubPay.Agent.Client.ViewModels;

namespace ClubPay.Agent.Client.Views;

public partial class PlayerDockWindow : Window
{
    public static PlayerDockWindow? Instance { get; private set; }
    private GameLauncherViewModel Vm => (GameLauncherViewModel)DataContext;

    public PlayerDockWindow(GameLauncherViewModel vm)
    {
        Instance = this;
        DataContext = vm;
        InitializeComponent();
    }

    internal void ShowDock()
    {
        PositionAtBottom();
        if (!IsVisible)
            Show();

        // The launcher is also topmost. Reapply the dock's z-order on every
        // return so it cannot slip behind the fullscreen window after a game
        // has been minimised.
        Topmost = false;
        Topmost = true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => PositionAtBottom();

    private void PositionAtBottom()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height;
        Width = SystemParameters.VirtualScreenWidth;
    }

    private void OnDockItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement dockItem || dockItem.DataContext is null)
            return;

        e.Handled = true;
        if (dockItem.ContextMenu is { } previousMenu)
            previousMenu.IsOpen = false;

        var menu = new ContextMenu { PlacementTarget = dockItem };
        var closeItem = new MenuItem
        {
            Command = Vm.CloseRunningAppCommand,
            CommandParameter = dockItem.DataContext
        };
        closeItem.SetBinding(MenuItem.HeaderProperty, new Binding("[CloseApplication]")
        {
            Source = Application.Current?.TryFindResource("Loc"),
            Mode = BindingMode.OneWay
        });
        menu.Items.Add(closeItem);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(dockItem.ContextMenu, menu))
                dockItem.ContextMenu = null;
        };

        dockItem.ContextMenu = menu;
        menu.IsOpen = true;
    }
}
