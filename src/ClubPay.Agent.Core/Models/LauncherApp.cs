using System.Security.Cryptography;
using System.Text;

namespace ClubPay.Agent.Core.Models;

public record LauncherApp(
    string  Name,
    string  ExePath,
    string  Args     = "",
    string  IconPath = "",
    string  Category = LauncherCategories.Other
)
{
    // Stable across Agent restarts and independent of a display label's locale.
    public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{Name.Trim().ToLowerInvariant()}\n{ExePath.Trim().ToLowerInvariant()}\n{Args.Trim().ToLowerInvariant()}"))).ToLowerInvariant();
    public string FirstChar => Name.Length > 0 ? Name[0].ToString().ToUpper() : "?";
};
