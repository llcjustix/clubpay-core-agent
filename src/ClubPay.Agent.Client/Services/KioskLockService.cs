using System.Runtime.InteropServices;
using Microsoft.Win32;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class KioskLockService : IKioskLockService
{
    private nint _hookHandle;
    // Field prevents GC from collecting the delegate while hook is active
    private NativeKiosk.LowLevelKeyboardProc? _hookProc;

    // volatile: hook callback runs on the message-pump thread, SetMode from UI thread
    private volatile KioskLockMode _mode = KioskLockMode.Full;

    public void Install()
    {
        ApplyRegistry(enable: true);
        _hookProc   = HookCallback;
        _hookHandle = NativeKiosk.SetWindowsHookEx(
            NativeKiosk.WH_KEYBOARD_LL,
            _hookProc,
            NativeKiosk.GetModuleHandle(null),
            0);
    }

    public void Uninstall()
    {
        if (_hookHandle != nint.Zero)
        {
            NativeKiosk.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = nint.Zero;
            _hookProc   = null;
        }
        ApplyRegistry(enable: false);
    }

    public void SetMode(KioskLockMode mode) => _mode = mode;

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var kb   = Marshal.PtrToStructure<NativeKiosk.KBDLLHOOKSTRUCT>(lParam);
            bool alt  = (NativeKiosk.GetKeyState(NativeKiosk.VK_MENU)    & 0x8000) != 0;
            bool ctrl = (NativeKiosk.GetKeyState(NativeKiosk.VK_CONTROL) & 0x8000) != 0;
            bool shft = (NativeKiosk.GetKeyState(NativeKiosk.VK_SHIFT)   & 0x8000) != 0;
            var  mode = _mode;

            bool block = kb.vkCode switch
            {
                // Always blocked — regardless of mode
                NativeKiosk.VK_LWIN                          => true,
                NativeKiosk.VK_RWIN                          => true,
                NativeKiosk.VK_ESCAPE when ctrl && shft      => true, // Ctrl+Shift+Esc (Task Mgr)
                NativeKiosk.VK_DELETE when ctrl && alt        => true, // Ctrl+Alt+Del (UI level)

                // Blocked only in Full mode (Locked/Frozen) — allowed during Active session
                NativeKiosk.VK_F4     when alt && mode == KioskLockMode.Full => true,
                NativeKiosk.VK_TAB    when alt && mode == KioskLockMode.Full => true,
                NativeKiosk.VK_ESCAPE when alt && mode == KioskLockMode.Full => true,

                _ => false
            };

            if (block) return -1;
        }
        return NativeKiosk.CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }

    private static void ApplyRegistry(bool enable)
    {
        // Registry policy keys may be protected on some Windows configurations.
        // Keyboard hook (above) is the primary lockdown; registry is best-effort.
        try
        {
            using var sys = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\System", writable: true)
                ?? Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\System");
            if (enable) sys.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
            else sys.DeleteValue("DisableTaskMgr", throwOnMissingValue: false);
        }
        catch { /* elevated rights not available */ }

        try
        {
            using var exp = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", writable: true)
                ?? Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
            if (enable) exp.SetValue("NoWinKeys", 1, RegistryValueKind.DWord);
            else exp.DeleteValue("NoWinKeys", throwOnMissingValue: false);
        }
        catch { /* elevated rights not available */ }
    }
}

internal static class NativeKiosk
{
    public const int WH_KEYBOARD_LL = 13;
    public const uint VK_LWIN = 0x5B;
    public const uint VK_RWIN = 0x5C;
    public const uint VK_F4 = 0x73;
    public const uint VK_TAB = 0x09;
    public const uint VK_ESCAPE = 0x1B;
    public const uint VK_DELETE = 0x2E;
    public const uint VK_MENU = 0x12; // Alt
    public const uint VK_CONTROL = 0x11;
    public const uint VK_SHIFT = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(uint nVirtKey);
}
