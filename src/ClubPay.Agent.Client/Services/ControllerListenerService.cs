using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class ControllerListenerService : IControllerListener
{
    private readonly HttpListener _http = new();
    private readonly string _secret;
    private readonly IAgentService _agent;
    private readonly ISessionStore _store;
    private readonly int _port;
    private CancellationTokenSource? _cts;

    public event Action<SessionStartCommand>? SessionStartReceived;
    public event Action? SessionEndReceived;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ControllerListenerService(IConfiguration config, IAgentService agent, ISessionStore store)
    {
        _agent = agent;
        _store = store;
        _port = int.TryParse(config["Agent:ControllerPort"], out var p) ? p : Constants.Controller.Port;
        _secret = config["Agent:AgentSecret"] ?? string.Empty;
        // localhost only — use a reverse proxy or netsh urlacl for external access
        _http.Prefixes.Add($"http://localhost:{_port}/");
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _http.Start();
        Task.Run(() => ListenLoop(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _http.Stop();
        return Task.CompletedTask;
    }

    private async Task ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _http.GetContextAsync(); }
            catch { break; }

            _ = Task.Run(() => HandleRequest(ctx), token);
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        try
        {
            // Secret check on all mutating routes
            if (req.HttpMethod != "GET")
            {
                var secret = req.Headers[Constants.Controller.SecretHeader];
                if (secret != _secret)
                {
                    await WriteJson(resp, 401, new { error = "unauthorized" });
                    return;
                }
            }

            var path = req.Url?.AbsolutePath.TrimEnd('/') ?? "/";

            if (req.HttpMethod == "GET" && path == "/status")
            {
                await HandleStatus(resp);
            }
            else if (req.HttpMethod == "POST" && path == "/cmd/start")
            {
                await HandleStart(req, resp);
            }
            else if (req.HttpMethod == "POST" && path == "/cmd/end")
            {
                SessionEndReceived?.Invoke();
                await WriteJson(resp, 200, new { ok = true });
            }
            else
            {
                await WriteJson(resp, 404, new { error = "not found" });
            }
        }
        catch (Exception ex)
        {
            await WriteJson(resp, 500, new { error = ex.Message });
        }
        finally
        {
            resp.Close();
        }
    }

    private async Task HandleStatus(HttpListenerResponse resp)
    {
        var session = _store.Current;
        int remaining = session?.RemainingSeconds(DateTime.UtcNow) ?? 0;
        string state = session is null ? "Locked"
                      : remaining > 0 ? "Active"
                      : "Frozen";
        await WriteJson(resp, 200, new
        {
            pcId = _agent.PcId,
            state,
            remainingSeconds = remaining
        });
    }

    private async Task HandleStart(HttpListenerRequest req, HttpListenerResponse resp)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        var body = await reader.ReadToEndAsync();

        var cmd = JsonSerializer.Deserialize<SessionStartCommand>(body, _json);
        if (cmd is null || cmd.GrantedSeconds <= 0)
        {
            await WriteJson(resp, 400, new { error = "invalid payload" });
            return;
        }

        SessionStartReceived?.Invoke(cmd);
        await WriteJson(resp, 200, new { ok = true, sessionId = cmd.SessionId });
    }

    private static async Task WriteJson(HttpListenerResponse resp, int status, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.StatusCode = status;
        resp.ContentType = "application/json; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
    }
}
