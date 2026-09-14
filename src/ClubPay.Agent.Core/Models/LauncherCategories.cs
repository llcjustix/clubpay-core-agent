namespace ClubPay.Agent.Core.Models;

/// <summary>Canonical categories stored by the Controller. Everything unknown is intentionally Other.</summary>
public static class LauncherCategories
{
    public const string Shooter = "shooter";
    public const string Strategy = "strategy";
    public const string Other = "other";

    private static readonly string[] ShooterTerms =
    ["counter-strike", "counter strike", "cs2", "valorant", "call of duty", "battlefield", "apex", "overwatch", "rainbow six", "fortnite", "pubg", "quake", "doom", "war thunder"];
    private static readonly string[] StrategyTerms =
    ["civilization", "age of empires", "age of mythology", "starcraft", "warcraft", "total war", "command & conquer", "stronghold", "heroes of might", "anno", "factorio", "rimworld"];

    public static string Classify(string? name)
    {
        var value = name?.Trim().ToLowerInvariant() ?? string.Empty;
        if (ShooterTerms.Any(term => value.Contains(term, StringComparison.Ordinal))) return Shooter;
        if (StrategyTerms.Any(term => value.Contains(term, StringComparison.Ordinal))) return Strategy;
        return Other;
    }

    public static string Normalize(string? category) => category?.Trim().ToLowerInvariant() switch
    {
        Shooter => Shooter,
        Strategy => Strategy,
        _ => Other,
    };
}
