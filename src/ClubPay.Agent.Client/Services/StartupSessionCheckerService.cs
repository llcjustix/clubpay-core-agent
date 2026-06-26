using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class StartupSessionCheckerService : IStartupSessionChecker, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<StartupSessionCheckerService> _logger;
    private readonly string _pcId;
    private readonly bool _disabled;

    public StartupSessionCheckerService(
        IConfiguration config,
        IAgentService agent,
        ILogger<StartupSessionCheckerService> logger)
    {
        _logger = logger;
        _pcId = agent.PcId;

        var baseUrl = config["Agent:ControllerBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("Agent:ControllerBaseUrl is not configured — startup session check disabled");
            _disabled = true;
            _http = new HttpClient();
            return;
        }

        _http = BuildClient(new HttpClientHandler(), baseUrl,
            config["Agent:AgentSecret"] ?? string.Empty);
    }

    // Internal constructor for unit tests — injects a mock HttpMessageHandler
    internal StartupSessionCheckerService(
        HttpMessageHandler handler,
        string baseUrl,
        string secret,
        string pcId,
        ILogger<StartupSessionCheckerService> logger)
    {
        _logger = logger;
        _pcId = pcId;
        _disabled = false;
        _http = BuildClient(handler, baseUrl, secret);
    }

    public async Task<SessionStartCommand?> CheckAsync(CancellationToken ct = default)
    {
        if (_disabled)
            return null;

        var url = string.Format(Constants.Controller.PendingSessionPath, _pcId);

        for (int attempt = 0; attempt < Constants.Controller.StartupCheckMaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt))
                          + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                await Task.Delay(delay, ct);
            }

            try
            {
                var response = await _http.GetAsync(url, ct);

                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    _logger.LogInformation("No pending session for {PcId}", _pcId);
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Startup check attempt {Attempt} returned {StatusCode}",
                        attempt + 1, (int)response.StatusCode);
                    continue;
                }

                var cmd = await response.Content.ReadFromJsonAsync<SessionStartCommand>(ct);
                if (cmd is null)
                {
                    _logger.LogWarning("Startup check attempt {Attempt} returned null payload", attempt + 1);
                    continue;
                }

                _logger.LogInformation("Pending session {SessionId} found for {PcId}",
                    cmd.SessionId, _pcId);
                return cmd;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Startup check attempt {Attempt} failed — network error", attempt + 1);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Startup session check cancelled");
                throw;
            }
        }

        _logger.LogWarning("Controller unreachable after {MaxRetries} attempts; staying Locked",
            Constants.Controller.StartupCheckMaxRetries);
        return null;
    }

    public void Dispose() => _http.Dispose();

    private static HttpClient BuildClient(HttpMessageHandler handler, string baseUrl, string secret)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
        client.DefaultRequestHeaders.Add(Constants.Controller.SecretHeader, secret);
        return client;
    }
}
