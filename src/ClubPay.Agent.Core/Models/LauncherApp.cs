namespace ClubPay.Agent.Core.Models;

public enum LauncherAppType
{
    Executable,
    SteamGame,
    Browser,
}

public record LauncherApp(
    string Name,
    string ExePath,
    string Args = "",
    string IconPath = "",
    string Category = "O'yin",
    string Id = "",
    LauncherAppType Type = LauncherAppType.Executable,
    int? SteamAppId = null,
    string WorkingDirectory = "",
    IReadOnlySet<string>? ProcessNames = null
)
{
    public string StableId => string.IsNullOrWhiteSpace(Id) ? Name : Id;

    public string FirstChar => Name.Length > 0 ? Name[0].ToString().ToUpper() : "?";

    public bool HasIcon => !string.IsNullOrWhiteSpace(IconPath) && File.Exists(IconPath);
}
