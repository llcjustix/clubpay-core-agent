namespace ClubPay.Agent.Core.Services;

public enum KioskLockMode
{
    /// <summary>Locked / Frozen — everything blocked (Win, Alt+F4, Alt+Tab, Ctrl+Shift+Esc)</summary>
    Full,
    /// <summary>Active session — Win/TaskMgr still blocked, but Alt+F4 and Alt+Tab allowed for games</summary>
    Session
}

public interface IKioskLockService
{
    void Install();
    void Uninstall();
    void SetMode(KioskLockMode mode);

    /// <summary>Ctrl+Shift+F9, only while in Session mode — toggles the in-session shell
    /// (GameLauncherWindow) without disturbing the running game (ТЗ §22 "по горячей клавише,
    /// не мешает игре").</summary>
    event Action? ShellToggleRequested;
}
