using System.Text.Json;
using ClubPay.Agent.TestHarness;

var server = new FakeControllerServer();
await server.StartAsync();

Console.WriteLine("ClubPay Mock Controller");
Console.WriteLine($"WebSocket URL: {server.WebSocketUrl}");
Console.WriteLine("Agent'ning appsettings.json faylida Controller:WebSocketUrl shu manzilga ko'rsatilishi kerak.");
Console.WriteLine("Agent ulanishini kutmoqda (Ctrl+C — chiqish)...");

var eventListenerCts = new CancellationTokenSource();
_ = Task.Run(async () =>
{
    while (!eventListenerCts.IsCancellationRequested)
    {
        try
        {
            var evt = await server.AwaitNextEventAsync(TimeSpan.FromDays(1), eventListenerCts.Token);
            Console.WriteLine($"\n[EVENT] {evt.Name} — {JsonSerializer.Serialize(evt.Payload)}");
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[EVENT-ERROR] {ex.Message}");
        }
    }
});

await server.WaitForConnectionAsync(TimeSpan.FromMinutes(10));
Console.WriteLine("Agent ulandi.\n");
PrintHelp();

while (true)
{
    Console.Write("\n> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input))
        continue;

    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var cmd = parts[0].ToLowerInvariant();

    if (cmd is "quit" or "exit")
        break;

    try
    {
        await RunCommandAsync(cmd, parts);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[XATO] {ex.Message}");
    }
}

eventListenerCts.Cancel();
await server.DisposeAsync();
return;

async Task RunCommandAsync(string cmd, string[] parts)
{
    switch (cmd)
    {
        case "help":
            PrintHelp();
            return;

        case "start":
            {
                int seconds = ParseIntArg(parts, 1, 3600);
                await SendAndPrintAsync("start_session", new
                {
                    external_pc_id = "pc-001",
                    grant_id = "grant_" + Guid.NewGuid().ToString("N")[..8],
                    granted_seconds = seconds,
                    ends_at = DateTime.UtcNow.AddSeconds(seconds),
                    zone = "Standard",
                    start_at = DateTime.UtcNow,
                    extend_url = "https://clubpay.justix.uz/qr/se_" + Guid.NewGuid().ToString("N")[..12],
                });
                return;
            }

        case "extend":
            {
                string coreSessionId = parts.Length > 1 ? parts[1] : "";
                int seconds = ParseIntArg(parts, 2, 1800);
                await SendAndPrintAsync("extend_session", new
                {
                    core_session_id = coreSessionId,
                    grant_id = "grant_" + Guid.NewGuid().ToString("N")[..8],
                    added_seconds = seconds,
                });
                return;
            }

        case "end":
            {
                string coreSessionId = parts.Length > 1 ? parts[1] : "";
                string reason = parts.Length > 2 ? parts[2].ToUpperInvariant() : "MANAGER";
                await SendAndPrintAsync("end_session", new { core_session_id = coreSessionId, reason });
                return;
            }

        case "lock":
            await SendAndPrintAsync("lock", new { external_pc_id = "pc-001", reason = parts.Length > 1 ? parts[1] : (string?)null });
            return;

        case "unlock":
            await SendAndPrintAsync("unlock", new { external_pc_id = "pc-001", reason = parts.Length > 1 ? parts[1] : (string?)null });
            return;

        case "sleep":
            await SendAndPrintAsync("sleep", new { external_pc_id = "pc-001" });
            return;

        case "wake":
            await SendAndPrintAsync("wake", new { external_pc_id = "pc-001" });
            return;

        case "repair":
            {
                bool on = parts.Length > 1 && parts[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                await SendAndPrintAsync("set_repair", new { external_pc_id = "pc-001", on });
                return;
            }

        case "status":
            await SendAndPrintAsync("get_status", new { external_pc_id = "pc-001" });
            return;

        default:
            Console.WriteLine($"Noma'lum buyruq: {cmd}. 'help' yozing.");
            return;
    }
}

async Task SendAndPrintAsync(string name, object payload)
{
    var commandId = "cmd_" + Guid.NewGuid().ToString("N")[..8];
    await server.SendCommandAsync(name, payload, commandId);
    var result = await server.AwaitNextCommandResultAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine($"[COMMAND_RESULT] {name} -> status={result.Status}" +
        (result.ErrorCode is { } code ? $", error_code={code}" : "") +
        (result.Message is { } msg ? $", message={msg}" : ""));
    if (result.Payload is not null)
        Console.WriteLine($"  payload: {JsonSerializer.Serialize(result.Payload)}");
}

static int ParseIntArg(string[] parts, int index, int fallback) =>
    parts.Length > index && int.TryParse(parts[index], out var v) ? v : fallback;

static void PrintHelp()
{
    Console.WriteLine("""
        Buyruqlar:
          start [seconds]                — start_session (default 3600s)
          extend <core_session_id> [sec] — extend_session (default 1800s)
          end <core_session_id> [reason] — end_session (TIME_UP|MANAGER|REFUND|CLIENT_LEFT|ERROR, default MANAGER)
          lock [reason]                  — lock
          unlock [reason]                — unlock
          sleep                          — sleep
          wake                           — wake
          repair on|off                  — set_repair
          status                         — get_status
          help                           — shu ro'yxat
          quit                           — chiqish
        """);
}
