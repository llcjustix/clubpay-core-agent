using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using ClubPay.Agent.Client.ViewModels;

namespace ClubPay.Agent.Client.Views;

public partial class PlayerDockWindow : Window
{
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    public static PlayerDockWindow? Instance { get; private set; }
    private GameLauncherViewModel Vm => (GameLauncherViewModel)DataContext;

    public PlayerDockWindow(GameLauncherViewModel vm)
    {
        Instance = this;
        DataContext = vm;
        InitializeComponent();
        Deactivated += (_, _) => Dispatcher.BeginInvoke(KeepAbovePlayerWindows);
    }

    internal void ShowDock()
    {
        PositionAtBottom();
        if (!IsVisible)
            Show();

        KeepAbovePlayerWindows();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => PositionAtBottom();

    internal void RefreshPosition()
    {
        PositionAtBottom();
        if (IsVisible)
        {
            KeepAbovePlayerWindows();
        }
    }

    internal void KeepAbovePlayerWindows()
    {
        if (!IsVisible)
            return;

        // WPF's Topmost property is not enough after Steam creates/reparents its
        // window or Explorer recreates the taskbar over RDP. Reapply HWND_TOPMOST
        // directly, with SWP_NOACTIVATE so the dock never steals keyboard focus
        // from the player application.
        Topmost = true;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoActivate | SwpShowWindow);
    }

    private void PositionAtBottom()
    {
        // The dock belongs to the primary player display. Using the virtual desktop
        // produced a clipped bar on mixed-DPI installations.
        Left = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Top = SystemParameters.PrimaryScreenHeight - Height;
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

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
