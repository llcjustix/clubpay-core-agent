using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class CoreApiListenerService : ICoreApiListener
{
    private readonly HttpListener _http = new();
    private readonly string _coreToken;
    private readonly IAgentService _agent;
    private readonly ISessionStore _store;
    private readonly IBillingEventReporter _reporter;
    private readonly ILogger<CoreApiListenerService> _logger;
    private readonly int _port;
    private CancellationTokenSource? _cts;

    public event Action<CoreSessionStartCommand>? SessionStartReceived;
    public event Action<CoreSessionExtendCommand>? SessionExtendReceived;
    public event Action<CoreSessionEndCommand>? SessionEndReceived;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public CoreApiListenerService(
        IConfiguration config,
        IAgentService agent,
        ISessionStore store,
        IBillingEventReporter reporter,
        ILogger<CoreApiListenerService> logger)
    {
        _agent = agent;
        _store = store;
        _reporter = reporter;
        _logger = logger;
        _port = int.TryParse(config["Billing:ListenerPort"], out var p) ? p : Constants.Billing.ListenerPort;
        _coreToken = config["Billing:CoreToken"] ?? string.Empty;
        // localhost only — Billing (cloud) cannot reach this directly without a tunnel/relay
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
            if (req.HttpMethod != "GET" && !IsAuthorized(req))
            {
                await WriteJson(resp, 401, new { error = "unauthorized" });
                return;
            }

            var segments = (req.Url?.AbsolutePath.Trim('/') ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (req.HttpMethod == "GET" && segments is ["core", "v1", "pcs", var pcId, "status"])
            {
                await HandleStatus(pcId, resp);
            }
            else if (req.HttpMethod == "POST" && segments is ["core", "v1", "sessions", "start"])
            {
                await HandleStart(req, resp);
            }
            else if (req.HttpMethod == "POST" && segments is ["core", "v1", "sessions", var coreSessionId, "extend"])
            {
                await HandleExtend(coreSessionId, req, resp);
            }
            else if (req.HttpMethod == "POST" && segments is ["core", "v1", "sessions", var coreSessionIdForEnd, "end"])
            {
                await HandleEnd(coreSessionIdForEnd, req, resp);
            }
            else
            {
                await WriteJson(resp, 404, new { error = "not found" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while processing Core API request {Path}", req.Url?.AbsolutePath);
            await _reporter.ReportCommandFailedAsync(req.Url?.AbsolutePath ?? "unknown", ex.Message);
            await WriteJson(resp, 500, new { error = ex.Message });
        }
        finally
        {
            resp.Close();
        }
    }

    private bool IsAuthorized(HttpListenerRequest req)
    {
        var header = req.Headers["Authorization"];
        return header == $"Bearer {_coreToken}";
    }

    private async Task HandleStatus(string externalPcId, HttpListenerResponse resp)
    {
        if (!string.Equals(externalPcId, _agent.ExternalPcId, StringComparison.OrdinalIgnoreCase))
        {
            await WriteJson(resp, 404, new { error = "not found" });
            return;
        }

        var session = _store.Current;
        int remaining = session?.RemainingSeconds(DateTime.UtcNow) ?? 0;
        string state = session is null ? "Locked"
                      : remaining > 0 ? "Active"
                      : "Frozen";
        await WriteJson(resp, 200, new
        {
            external_pc_id = _agent.ExternalPcId,
            state,
            remaining_seconds = remaining
        });
    }

    private async Task HandleStart(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var body = await ReadBodyAsync(req);
        var cmd = TryDeserialize<CoreSessionStartCommand>(body);

        if (cmd is null || cmd.GrantedSeconds <= 0 || string.IsNullOrWhiteSpace(cmd.ExternalPcId))
        {
            _logger.LogWarning("Rejected /core/v1/sessions/start with invalid payload: {Body}", body);
            await _reporter.ReportCommandFailedAsync("sessions/start", "invalid payload");
            await WriteJson(resp, 400, new { error = "invalid payload" });
            return;
        }

        SessionStartReceived?.Invoke(cmd);
        await WriteJson(resp, 200, new { ok = true, core_session_id = cmd.CoreSessionId });
    }

    private async Task HandleExtend(string coreSessionId, HttpListenerRequest req, HttpListenerResponse resp)
    {
        if (!IsActiveCoreSession(coreSessionId))
        {
            await WriteJson(resp, 404, new { error = "session not found" });
            return;
        }

        var body = await ReadBodyAsync(req);
        var cmd = TryDeserialize<CoreSessionExtendCommand>(body);

        if (cmd is null || cmd.AdditionalSeconds <= 0)
        {
            _logger.LogWarning("Rejected /core/v1/sessions/{CoreSessionId}/extend with invalid payload: {Body}",
                coreSessionId, body);
            await _reporter.ReportCommandFailedAsync("sessions/extend", "invalid payload");
            await WriteJson(resp, 400, new { error = "invalid payload" });
            return;
        }

        SessionExtendReceived?.Invoke(cmd);
        await WriteJson(resp, 200, new { ok = true });
    }

    private async Task HandleEnd(string coreSessionId, HttpListenerRequest req, HttpListenerResponse resp)
    {
        if (!IsActiveCoreSession(coreSessionId))
        {
            await WriteJson(resp, 404, new { error = "session not found" });
            return;
        }

        var body = await ReadBodyAsync(req);
        var cmd = TryDeserialize<CoreSessionEndCommand>(body) ?? new CoreSessionEndCommand(null);

        SessionEndReceived?.Invoke(cmd);
        await WriteJson(resp, 200, new { ok = true });
    }

    private bool IsActiveCoreSession(string coreSessionId) =>
        Guid.TryParse(coreSessionId, out var id) && _store.Current?.CoreSessionId == id;

    private static async Task<string> ReadBodyAsync(HttpListenerRequest req)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        return await reader.ReadToEndAsync();
    }

    private T? TryDeserialize<T>(string body) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, _json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Core API request body: {Body}", body);
            return null;
        }
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
