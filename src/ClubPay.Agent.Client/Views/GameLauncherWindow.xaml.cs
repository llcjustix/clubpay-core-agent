using System.Windows;
using System.Windows.Interop;
using ClubPay.Agent.Client.ViewModels;

namespace ClubPay.Agent.Client.Views;

public partial class GameLauncherWindow : Window
{
    public static GameLauncherWindow? Instance { get; private set; }

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

    internal void EnterExternalAppMode() => SetNoActivate(true);

    internal void EnterLauncherMode() => SetNoActivate(false);

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
