using System.Windows;

namespace ClubPay.Agent.Client.Views;

/// <summary>
/// WindowState=Maximized is intentionally limited to Windows' work area, leaving
/// the taskbar strip visible. ClubPay windows must occupy the actual player
/// display.  A player Agent is single-screen; using the virtual desktop here
/// makes an RDP session retain stale dimensions after its window is resized.
/// </summary>
internal static class FullScreenWindow
{
    public static void CoverPrimaryScreen(Window window)
    {
        window.WindowState = WindowState.Normal;
        // PrimaryScreen includes the taskbar strip, unlike WorkArea. Its metrics
        // are refreshed by Windows when an RDP client changes its resolution.
        window.Left = 0;
        window.Top = 0;
        window.Width = SystemParameters.PrimaryScreenWidth;
        window.Height = SystemParameters.PrimaryScreenHeight;
    }
}
