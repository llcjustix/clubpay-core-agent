using ClubPay.Agent.Core.Models;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Discovers the small, explicitly approved set of non-Steam desktop applications
/// that can be exposed in the player launcher. This is deliberately an allow-list:
/// enumerating every installed Windows program would let a player escape the kiosk.
/// </summary>
public sealed class DesktopAppDiscoveryService(IConfiguration config)
{
    public IReadOnlyList<LauncherApp> Discover()
    {
        var apps = new List<LauncherApp>();

        if (config.GetValue("Launcher:DiscoverDiscord", true))
        {
            var discordUpdater = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Discord",
                "Update.exe");

            // Discord replaces its versioned Discord.exe on updates. Its supported
            // Update.exe entry point stays stable, so the tile survives client updates.
            if (File.Exists(discordUpdater))
            {
                apps.Add(new LauncherApp(
                    Name: "Discord",
                    ExePath: discordUpdater,
                    Args: "--processStart Discord.exe",
                    Category: LauncherCategories.Other));
            }
        }

        return apps;
    }
}
