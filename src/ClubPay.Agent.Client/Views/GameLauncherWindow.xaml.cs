using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Input;
using ClubPay.Agent.Client.ViewModels;

namespace ClubPay.Agent.Client.Views;

public partial class GameLauncherWindow : Window
{
    public static GameLauncherWindow? Instance { get; private set; }
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private bool _externalAppMode;

    internal GameLauncherViewModel Vm => (GameLauncherViewModel)DataContext;

    public GameLauncherWindow(GameLauncherViewModel vm)
    {
        Instance    = this;
        DataContext = vm;
        InitializeComponent();

        // Keep Agent as the protected fullscreen background, but make that background unable
        // to steal activation while Steam/a game is in front. A click outside a windowed game
        // must therefore stay with the external application instead of hiding it behind Agent.
        vm.AppLaunched += _ => Dispatcher.Invoke(() =>
        {
            if (!IsVisible)
                Show();
            EnterExternalAppMode();
        });

        // Game exited or user clicked "return" → show launcher again
        vm.ReturnRequested += () => Dispatcher.Invoke(() =>
        {
            EnterLauncherMode();
            Show();
            Activate();
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => FullScreenWindow.CoverPrimaryScreen(this);

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
        closeItem.SetBinding(
            MenuItem.HeaderProperty,
            new Binding("[CloseApplication]")
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
    }

    internal void EnterExternalAppMode()
    {
        _externalAppMode = true;
        SetNoActivate(true);
    }

    internal void EnterLauncherMode()
    {
        _externalAppMode = false;
        SetNoActivate(false);
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // WS_EX_NOACTIVATE alone is not reliable for every WPF/Windows combination.
        // Explicitly reject activation when a player clicks the launcher background:
        // Steam stays in front rather than looking as if it has disappeared.
        if (_externalAppMode && message == WmMouseActivate)
        {
            handled = true;
            return MaNoActivate;
        }

        return nint.Zero;
    }

    private void SetNoActivate(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeWindowActivation.SetNoActivate(hwnd, enabled);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Launcher never closes during a session — only hides
        e.Cancel = true;
        Hide();
    }
}

internal static class NativeWindowActivation
{
    internal const int WsExNoActivate = 0x08000000;
    private const int GwlExStyle = -20;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    internal static int WithNoActivate(int style, bool enabled) =>
        enabled ? style | WsExNoActivate : style & ~WsExNoActivate;

    internal static void SetNoActivate(nint hwnd, bool enabled)
    {
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, WithNoActivate(style, enabled));
        SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hwnd, int index, int newStyle);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
