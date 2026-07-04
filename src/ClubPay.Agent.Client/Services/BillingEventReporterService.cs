using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class BillingEventReporterService : IBillingEventReporter, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<BillingEventReporterService> _logger;
    private readonly string _externalPcId;
    private readonly bool _disabled;

    public BillingEventReporterService(
        IConfiguration config,
        IAgentService agent,
        ILogger<BillingEventReporterService> logger)
    {
        _logger = logger;
        _externalPcId = agent.ExternalPcId;

        var baseUrl = config["Billing:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("Billing:BaseUrl is not configured — event reporting disabled");
            _disabled = true;
            _http = new HttpClient();
            return;
        }

        _http = BuildClient(new HttpClientHandler(), baseUrl, config["Billing:CoreToken"] ?? string.Empty);
    }

    // Internal constructor for unit tests — injects a mock HttpMessageHandler
    internal BillingEventReporterService(
        HttpMessageHandler handler,
        string baseUrl,
        string coreToken,
        string externalPcId,
        ILogger<BillingEventReporterService> logger)
    {
        _logger = logger;
        _externalPcId = externalPcId;
        _disabled = false;
        _http = BuildClient(handler, baseUrl, coreToken);
    }

    public Task ReportPcStatusChangedAsync(string state, CancellationToken ct = default) =>
        SendEventAsync(Constants.Billing.EventType.PcStatusChanged, new { state }, ct);

    public Task ReportSessionStartedAsync(Guid sessionId, CancellationToken ct = default) =>
        SendEventAsync(Constants.Billing.EventType.SessionStarted, new { session_id = sessionId }, ct);

    public Task ReportSessionEndedAsync(Guid sessionId, string? reason, CancellationToken ct = default) =>
        SendEventAsync(Constants.Billing.EventType.SessionEnded, new { session_id = sessionId, reason }, ct);

    public Task ReportSessionFailedAsync(Guid? sessionId, string reason, CancellationToken ct = default) =>
        SendEventAsync(Constants.Billing.EventType.SessionFailed, new { session_id = sessionId, reason }, ct);

    public Task ReportCommandFailedAsync(string command, string reason, CancellationToken ct = default) =>
        SendEventAsync(Constants.Billing.EventType.CommandFailed, new { command, reason }, ct);

    private async Task SendEventAsync(string eventType, object data, CancellationToken ct)
    {
        if (_disabled)
            return;

        var payload = new CoreEvent(eventType, _externalPcId, DateTime.UtcNow, data);

        for (int attempt = 0; attempt < Constants.Billing.EventReportMaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt))
                          + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { return; }
            }

            try
            {
                var response = await _http.PostAsJsonAsync(Constants.Billing.EventsPath, payload, ct);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Reported {EventType} event to Billing", eventType);
                    return;
                }

                _logger.LogWarning("Reporting {EventType} attempt {Attempt} returned {StatusCode}",
                    eventType, attempt + 1, (int)response.StatusCode);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Reporting {EventType} attempt {Attempt} failed — network error",
                    eventType, attempt + 1);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Reporting {EventType} cancelled", eventType);
                return;
            }
        }

        _logger.LogWarning("Failed to report {EventType} to Billing after {MaxRetries} attempts",
            eventType, Constants.Billing.EventReportMaxRetries);
    }

    public void Dispose() => _http.Dispose();

    private static HttpClient BuildClient(HttpMessageHandler handler, string baseUrl, string coreToken)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", coreToken);
        return client;
    }
}
