using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Services;

public sealed class AgentEndpointServer : IDisposable
{
    private readonly HttpListener _http = new();
    private readonly IPendingSessionStore _store;
    private readonly ILogger<AgentEndpointServer> _logger;
    private readonly string _secret;
    private CancellationTokenSource? _cts;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AgentEndpointServer(
        IConfiguration config,
        IPendingSessionStore store,
        ILogger<AgentEndpointServer> logger)
    {
        _store = store;
        _logger = logger;
        _secret = config["Controller:AgentSecret"] ?? string.Empty;

        int port = int.TryParse(config["Controller:Port"], out var p) ? p : 8080;
        _http.Prefixes.Add($"http://+:{port}/");
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _http.Start();
        Task.Run(() => ListenLoop(_cts.Token), _cts.Token);
        _logger.LogInformation("AgentEndpointServer started on {Prefix}", _http.Prefixes.First());
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _http.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _http.Close();
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
            var secret = req.Headers[Constants.Controller.SecretHeader];
            if (secret != _secret)
            {
                await WriteJson(resp, 401, new { error = "unauthorized" });
                return;
            }

            var path = req.Url?.AbsolutePath.TrimEnd('/') ?? "/";

            // GET /api/agents/{pcId}/pending-session
            if (req.HttpMethod == "GET" && TryGetPcId(path, out var pcId))
            {
                await HandlePendingSession(resp, pcId!);
            }
            else
            {
                await WriteJson(resp, 404, new { error = "not found" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling agent endpoint request");
            await WriteJson(resp, 500, new { error = ex.Message });
        }
        finally
        {
            resp.Close();
        }
    }

    private async Task HandlePendingSession(HttpListenerResponse resp, string pcId)
    {
        var cmd = _store.Get(pcId);
        if (cmd is null)
        {
            resp.StatusCode = 204;
            resp.Close();
            return;
        }

        _store.Clear(pcId);
        _logger.LogInformation("Delivering pending session {SessionId} to {PcId}", cmd.SessionId, pcId);
        await WriteJson(resp, 200, cmd);
    }

    // Matches: /api/agents/{pcId}/pending-session
    private static bool TryGetPcId(string path, out string? pcId)
    {
        pcId = null;
        var prefix = "/api/agents/";
        var suffix = "/pending-session";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        pcId = path[prefix.Length..^suffix.Length];
        return !string.IsNullOrWhiteSpace(pcId);
    }

    private static async Task WriteJson(HttpListenerResponse resp, int status, object payload)
    {
        var json = JsonSerializer.Serialize(payload, _json);
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.StatusCode = status;
        resp.ContentType = "application/json; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
    }
}
