using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Keeps the Windows shell out of the customer-facing experience when Explorer is
/// still running (for example on a development VM). Production installations use
/// shell replacement as well, but hiding the taskbar here is an important second
/// line of defence and prevents it appearing beneath Steam or a windowed game.
/// </summary>
public interface IWindowsShellService
{
    void HideTaskbars();
    void RestoreTaskbars();
}

public sealed class WindowsShellService : IWindowsShellService
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;

    private readonly bool _enabled;
    private readonly HashSet<nint> _hiddenTaskbars = [];

    public WindowsShellService(IConfiguration configuration)
    {
        // This is deliberately independent from KioskLockdownEnabled. A testing
        // machine may allow the maintenance hotkey while still presenting no
        // Windows taskbar to a customer.
        _enabled = configuration.GetValue("Agent:HideWindowsTaskbar", true);
    }

    public void HideTaskbars()
    {
        if (!_enabled)
            return;

        foreach (var taskbar in FindTaskbars())
        {
            if (NativeShell.IsWindowVisible(taskbar))
            {
                NativeShell.ShowWindow(taskbar, SwHide);
                _hiddenTaskbars.Add(taskbar);
            }
        }
    }

    public void RestoreTaskbars()
    {
        // A previous Agent instance may have hidden the taskbar and then been
        // terminated before it could run OnExit.  Do not rely solely on the
        // handles remembered by this process: the shell must always be
        // restored when leaving kiosk mode.
        foreach (var taskbar in FindTaskbars())
        {
            NativeShell.ShowWindow(taskbar, SwRestore);
            NativeShell.ShowWindow(taskbar, SwShow);
        }

        foreach (var taskbar in _hiddenTaskbars)
        {
            NativeShell.ShowWindow(taskbar, SwRestore);
            NativeShell.ShowWindow(taskbar, SwShow);
        }

        _hiddenTaskbars.Clear();
    }

    private static IEnumerable<nint> FindTaskbars()
    {
        var primary = NativeShell.FindWindow("Shell_TrayWnd", null);
        if (primary != nint.Zero)
            yield return primary;

        nint current = nint.Zero;
        while ((current = NativeShell.FindWindowEx(
                   nint.Zero, current, "Shell_SecondaryTrayWnd", null)) != nint.Zero)
        {
            yield return current;
        }
    }
}

internal static class NativeShell
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowEx(
        nint hwndParent,
        nint hwndChildAfter,
        string lpszClass,
        string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);
}
