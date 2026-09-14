using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// A tiny local, non-secret heartbeat for the detached updater. A running
/// process is not sufficient evidence of a healthy kiosk: it must reconnect
/// to its Controller after a replacement. The file is intentionally local so
/// rollback works even while the club has no Internet connection.
/// </summary>
public static class AgentRuntimeHealth
{
    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "runtime", "agent-health.json");

    public static void MarkControllerConnected()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            var payload = JsonSerializer.Serialize(new
            {
                status = "connected",
                version,
                observed_at = DateTimeOffset.UtcNow.ToString("O"),
            });
            File.WriteAllText(Path, payload);
        }
        catch
        {
            // A health marker must never prevent the kiosk from reconnecting.
        }
    }
}
