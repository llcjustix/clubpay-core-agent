using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.IO;
using System.Text.RegularExpressions;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Reads the Steam libraries installed on this PC. ClubPay never downloads games or handles
/// licences: it only exposes games that Steam has already installed for the current Windows user.
/// </summary>
public sealed class SteamGameDiscoveryService(IConfiguration config, ILogger<SteamGameDiscoveryService> logger)
{
    private static readonly Regex VdfPath = new("\\\"path\\\"\\s+\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ManifestAppId = new("\\\"appid\\\"\\s+\\\"(?<value>\\d+)\\\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ManifestName = new("\\\"name\\\"\\s+\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<LauncherApp> Discover()
    {
        if (!config.GetValue("Launcher:DiscoverSteamGames", true))
            return [];

        var roots = GetLibraryRoots();
        var steamExe = roots
            .Select(root => Path.Combine(root, "Steam.exe"))
            .FirstOrDefault(File.Exists);

        if (string.IsNullOrWhiteSpace(steamExe))
            return [];

        // Do not expose the Steam client itself as a player-facing tile. Opening the client only
        // gives the player the regular Windows/Steam UI (and is what made the VM look as though
        // the Agent had been minimised). The kiosk launcher must expose installed games only.
        var apps = new List<LauncherApp>();

        foreach (var manifest in roots.SelectMany(GetManifestFiles))
        {
            try
            {
                var content = File.ReadAllText(manifest);
                var appId = ManifestAppId.Match(content).Groups["value"].Value;
                var name = ManifestName.Match(content).Groups["value"].Value;
                if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name))
                    continue;

                // This is a Steam runtime dependency, not a game a player can launch.
                // Steam may create its manifest automatically, so never expose it as a tile.
                if (appId == "228980" || name.Equals("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase))
                    continue;

                apps.Add(new LauncherApp(name, steamExe, $"-applaunch {appId}", Category: "O'yin"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not read Steam manifest {Manifest}", manifest);
            }
        }

        return apps
            .GroupBy(app => app.Args, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private HashSet<string> GetLibraryRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuredRoot in config.GetSection("Launcher:SteamLibraryRoots").GetChildren())
            AddRoot(roots, configuredRoot.Value);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            AddRoot(roots, key?.GetValue("SteamPath") as string);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not read the Steam registry path");
        }

        AddRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        AddRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));

        foreach (var root in roots.ToArray())
        {
            var libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile))
                continue;

            try
            {
                foreach (Match match in VdfPath.Matches(File.ReadAllText(libraryFile)))
                    AddRoot(roots, match.Groups["value"].Value.Replace("\\\\", "\\"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not read Steam library config {Path}", libraryFile);
            }
        }

        return roots;
    }

    private static void AddRoot(ISet<string> roots, string? root)
    {
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            roots.Add(root.Trim());
    }

    private static IEnumerable<string> GetManifestFiles(string root)
    {
        var steamApps = Path.Combine(root, "steamapps");
        return Directory.Exists(steamApps)
            ? Directory.EnumerateFiles(steamApps, "appmanifest_*.acf")
            : [];
    }
}
