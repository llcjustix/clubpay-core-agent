using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Client.ViewModels;

namespace ClubPay.Agent.Client.Views;

public partial class GameLauncherWindow : Window
{
    public static GameLauncherWindow? Instance { get; private set; }
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private readonly IClientSessionEndService _sessionEnd;
    private readonly QrCodeService _qr;
    private readonly LocalizationService _localizer;
    private readonly IWindowsShellService _windowsShell;
    private readonly DispatcherTimer _externalAppUiTimer;
    private bool _externalAppMode;

    public GameLauncherWindow(
        GameLauncherViewModel vm,
        MainViewModel main,
        IClientSessionEndService sessionEnd,
        QrCodeService qr,
        LocalizationService localizer,
        IWindowsShellService windowsShell)
    {
        Instance    = this;
        DataContext = vm;
        _sessionEnd = sessionEnd;
        _qr = qr;
        _localizer = localizer;
        _windowsShell = windowsShell;
        InitializeComponent();
        SessionCard.DataContext = main.ActiveSession;
        SessionCard.EndSessionRequested += RequestSessionEndAsync;

        // Explorer and Steam can both reorder windows *after* their initial
        // foreground transition.  A one-time 500 ms correction is therefore not
        // sufficient: keep only the customer-facing surfaces reinforced while an
        // external application is open, without ever activating the Agent window.
        _externalAppUiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _externalAppUiTimer.Tick += (_, _) => KeepExternalAppSurfaceStable();

        // Keep Agent as the protected fullscreen background, but make that background unable
        // to steal activation while Steam/a game is in front. A click outside a windowed game
        // must therefore stay with the external application instead of hiding it behind Agent.
        vm.AppLaunchRequested += _ => Dispatcher.Invoke(() => PlayerDockWindow.Instance?.ShowDock());
        vm.ExternalAppPreparationRequested += _ => Dispatcher.Invoke(EnterExternalAppMode);
        vm.AppLaunched += _ => Dispatcher.Invoke(() =>
        {
            // Keep the launcher rendered as the fallback background. If an app is
            // slow, minimised, or returns a transient HWND, the player never gets
            // a blank desktop with only the dock left behind.
            PlayerDockWindow.Instance?.ShowDock();
            KeepExternalAppSurfaceStable();
            if (!_externalAppUiTimer.IsEnabled)
                _externalAppUiTimer.Start();
        });

        // Game exited or user clicked "return" → show launcher again
        vm.ReturnRequested += () => Dispatcher.Invoke(() =>
        {
            ShowLauncherSurface();
            PlayerDockWindow.Instance?.ShowDock();
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
        => FullScreenWindow.CoverPrimaryScreen(this);

    internal void ShowLauncherSurface()
    {
        EnterLauncherMode();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        if (!IsVisible)
            Show();

        FullScreenWindow.CoverPrimaryScreen(this);
        // Toggle the flag so Windows reapplies the topmost z-order even after the
        // launcher was hidden while a Steam window had the foreground.
        Topmost = false;
        Topmost = true;
        Activate();
        Focus();
        _windowsShell.HideTaskbars();
    }

    private async Task RequestSessionEndAsync()
    {
        var dialog = new EndSessionDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var result = await _sessionEnd.EndCurrentSessionAsync();
            if (result.HasVoucherToShow)
                new VoucherDeliveryDialog(result, _qr).ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(_localizer.Format("EndSessionFailed", ex.Message), "ClubPay",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        // The dock is a separate topmost window. This full-screen window can now
        // drop behind Steam instead of covering it with an opaque surface.
        Topmost = false;
        SetNoActivate(true);
        _windowsShell.HideTaskbars();
    }

    private void KeepExternalAppSurfaceStable()
    {
        if (!_externalAppMode)
            return;

        // Do not call Activate/Focus here: this is deliberately a non-activating
        // z-order correction so a player can type in Steam or a game uninterrupted.
        _windowsShell.HideTaskbars();
        PlayerDockWindow.Instance?.KeepAbovePlayerWindows();
    }

    internal void EnterLauncherMode()
    {
        _externalAppMode = false;
        _externalAppUiTimer.Stop();
        SetNoActivate(false);
        Topmost = true;
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
