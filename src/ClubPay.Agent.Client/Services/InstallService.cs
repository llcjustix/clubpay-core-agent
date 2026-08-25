using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using Microsoft.Win32;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Handles one-time installation: file copy, Windows Startup registration,
/// auto-login setup, and optional shell replacement.
/// Must run elevated (admin) for HKLM registry writes.
/// Usage:
///   ClubPay.Agent.Client.exe --install [--shell]
///   ClubPay.Agent.Client.exe --setup-kiosk=username:password
///   ClubPay.Agent.Client.exe --uninstall
///   ClubPay.Agent.Client.exe --autologin=kiosk:password
/// </summary>
public static class InstallService
{
    public const string InstallDir   = @"C:\ClubPay\Agent";
    public const string ExeName      = "ClubPay.Agent.Client.exe";

    private const string RunKeyPath   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ClubPayAgent";
    private const string WinlogonKey  = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

    public static bool IsElevated =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

    public static void RelaunchElevated(IEnumerable<string> args)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = Environment.ProcessPath!,
            Arguments       = string.Join(" ", args),
            Verb            = "runas",         // UAC prompt
            UseShellExecute = true
        });
    }

    // ─── INSTALL ─────────────────────────────────────────────────────────────

    public static void Install(bool setShellReplacement = false)
    {
        CopyFiles();
        RegisterStartup();
        if (setShellReplacement)
            SetShell(Path.Combine(InstallDir, ExeName));
    }

    private static void CopyFiles()
    {
        var srcDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        Directory.CreateDirectory(InstallDir);

        foreach (var src in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var rel  = Path.GetRelativePath(srcDir, src);
            var dest = Path.Combine(InstallDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, overwrite: true);
        }
    }

    private static void RegisterStartup()
    {
        var exePath = $"\"{Path.Combine(InstallDir, ExeName)}\"";
        using var run = Registry.LocalMachine.OpenSubKey(RunKeyPath, writable: true)
                     ?? throw new InvalidOperationException("Run registry key not accessible");
        run.SetValue(RunValueName, exePath);
    }

    private static void SetShell(string exePath)
    {
        using var wl = Registry.LocalMachine.OpenSubKey(WinlogonKey, writable: true)
                    ?? throw new InvalidOperationException("Winlogon key not accessible");
        wl.SetValue("Shell", exePath);
    }

    // ─── UNINSTALL ────────────────────────────────────────────────────────────

    public static void Uninstall()
    {
        // Remove from Startup
        using (var run = Registry.LocalMachine.OpenSubKey(RunKeyPath, writable: true))
            run?.DeleteValue(RunValueName, throwOnMissingValue: false);

        // Restore Shell → explorer.exe if we replaced it
        using (var wl = Registry.LocalMachine.OpenSubKey(WinlogonKey, writable: true))
        {
            var shell = wl?.GetValue("Shell") as string ?? "explorer.exe";
            if (!shell.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                wl!.SetValue("Shell", "explorer.exe");
        }

        // Remove install directory (best-effort — may fail if exe is running)
        try { Directory.Delete(InstallDir, recursive: true); }
        catch { /* running from install dir; files cleaned on next boot */ }
    }

    // ─── AUTO-LOGIN ───────────────────────────────────────────────────────────

    /// <summary>
    /// Configures Windows to log in automatically with the given user account.
    /// The password is stored in plaintext in the registry (standard Windows kiosk approach).
    /// For maximum security, use a dedicated account with no network permissions.
    /// </summary>
    public static void SetupAutoLogin(string username, string password = "")
    {
        using var wl = Registry.LocalMachine.OpenSubKey(WinlogonKey, writable: true)
                    ?? throw new InvalidOperationException("Winlogon key not accessible");
        wl.SetValue("AutoAdminLogon",    "1");
        wl.SetValue("DefaultUserName",   username);
        wl.SetValue("DefaultPassword",   password);
        wl.SetValue("DefaultDomainName", Environment.MachineName);
    }

    public static void DisableAutoLogin()
    {
        using var wl = Registry.LocalMachine.OpenSubKey(WinlogonKey, writable: true);
        wl?.SetValue("AutoAdminLogon", "0");
        wl?.DeleteValue("DefaultPassword", throwOnMissingValue: false);
    }

    // ─── STATUS ───────────────────────────────────────────────────────────────

    public static InstallStatus GetStatus()
    {
        var exePath = Path.Combine(InstallDir, ExeName);
        bool installed = File.Exists(exePath);

        string? startupValue;
        using (var run = Registry.LocalMachine.OpenSubKey(RunKeyPath))
            startupValue = run?.GetValue(RunValueName) as string;

        string? shell;
        string? autoLoginUser;
        bool    autoLoginEnabled;
        using (var wl = Registry.LocalMachine.OpenSubKey(WinlogonKey))
        {
            shell            = wl?.GetValue("Shell") as string ?? "explorer.exe";
            autoLoginUser    = wl?.GetValue("DefaultUserName") as string;
            autoLoginEnabled = (wl?.GetValue("AutoAdminLogon") as string) == "1";
        }

        return new InstallStatus(
            installed,
            startupValue != null,
            !shell.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase),
            autoLoginEnabled,
            autoLoginUser);
    }
}

public record InstallStatus(
    bool   FilesInstalled,
    bool   StartupRegistered,
    bool   ShellReplaced,
    bool   AutoLoginEnabled,
    string? AutoLoginUser);
