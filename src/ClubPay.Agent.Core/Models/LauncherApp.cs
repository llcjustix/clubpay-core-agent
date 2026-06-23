namespace ClubPay.Agent.Core.Models;

public record LauncherApp(
    string  Name,
    string  ExePath,
    string  Args     = "",
    string  IconPath = "",
    string  Category = "O'yin"
)
{
    public string FirstChar => Name.Length > 0 ? Name[0].ToString().ToUpper() : "?";
};
