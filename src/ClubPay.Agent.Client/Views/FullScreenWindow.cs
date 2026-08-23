using System.Windows;

namespace ClubPay.Agent.Client.Views;

/// <summary>
/// WindowState=Maximized is intentionally limited to Windows' work area, leaving
/// the taskbar strip visible. ClubPay windows must occupy the actual display.
/// </summary>
internal static class FullScreenWindow
{
    public static void CoverPrimaryScreen(Window window)
    {
        window.WindowState = WindowState.Normal;
        // VirtualScreen also covers the taskbar strip, unlike WorkArea, and makes
        // the agent a proper background on multi-monitor club PCs.
        window.Left = SystemParameters.VirtualScreenLeft;
        window.Top = SystemParameters.VirtualScreenTop;
        window.Width = SystemParameters.VirtualScreenWidth;
        window.Height = SystemParameters.VirtualScreenHeight;
    }
}
