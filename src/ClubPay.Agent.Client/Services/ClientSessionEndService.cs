using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class ClientSessionEndService : IClientSessionEndService
{
    private readonly ISessionCoordinator _coordinator;
    private readonly IAgentService _agent;
    private readonly string _agentToken;
    private readonly IReadOnlyList<Uri> _endpoints;

    public ClientSessionEndService(
        ISessionCoordinator coordinator,
        IAgentService agent,
        IConfiguration configuration)
    {
        _coordinator = coordinator;
        _agent = agent;
        _agentToken = configuration["Controller:AgentToken"] ?? string.Empty;
        _endpoints = BuildEndpoints(configuration);
    }

    public async Task<ClientSessionEndResult> EndCurrentSessionAsync(CancellationToken ct = default)
    {
        var session = _coordinator.CurrentSession
            ?? throw new InvalidOperationException("Active session was not found");
        if (session.CoreSessionId is not { } coreSessionId)
            throw new InvalidOperationException("Session is not linked to Core");
        if (_endpoints.Count == 0)
            throw new InvalidOperationException("Core session-end endpoint is not configured");

        var body = JsonSerializer.Serialize(new
        {
            external_pc_id = _agent.ExternalPcId,
            core_session_id = coreSessionId.ToString("N"),
        });
        string? lastError = null;
        foreach (var endpoint in _endpoints)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _agentToken);

                using var response = await client.SendAsync(request, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    lastError = ReadString(responseBody, "error") ?? "Could not end the session";
                    continue;
                }

                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;
                var voucher = root.TryGetProperty("voucher", out var voucherElement) ? voucherElement : default;
                var delivery = root.TryGetProperty("voucher_delivery", out var deliveryElement) ? deliveryElement : default;
                return new ClientSessionEndResult(
                    IsProfileSession: root.TryGetProperty("player_profile", out var playerProfile) && playerProfile.ValueKind == JsonValueKind.True,
                    VoucherCode: voucher.ValueKind == JsonValueKind.Object ? ReadString(voucher, "code") : null,
                    VoucherSeconds: voucher.ValueKind == JsonValueKind.Object ? ReadInt(voucher, "seconds_left") : 0,
                    ProfileBalanceAddedSeconds: root.TryGetProperty("player_balance", out var playerBalance) && playerBalance.ValueKind == JsonValueKind.Object
                        ? ReadInt(playerBalance, "seconds_added")
                        : 0,
                    DeliveryStatus: delivery.ValueKind == JsonValueKind.Object ? ReadString(delivery, "status") ?? "not_requested" : "not_requested",
                    TelegramLink: delivery.ValueKind == JsonValueKind.Object ? ReadString(delivery, "telegram_link") : null,
                    TelegramBotUsername: delivery.ValueKind == JsonValueKind.Object ? ReadString(delivery, "telegram_bot_username") : null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        throw new InvalidOperationException(lastError ?? "Could not end the session");
    }

    private static IReadOnlyList<Uri> BuildEndpoints(IConfiguration configuration)
    {
        var endpoints = new List<Uri>();
        AddEndpoint(endpoints, configuration["Controller:SessionEndUrl"]);
        foreach (var child in configuration.GetSection("Controller:FallbackSessionEndUrls").GetChildren())
            AddEndpoint(endpoints, child.Value);
        foreach (var value in (configuration["Controller:FallbackSessionEndUrls"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            AddEndpoint(endpoints, value);

        if (endpoints.Count > 0)
            return endpoints;

        AddBootstrapEndpoint(endpoints, configuration["Controller:BootstrapUrl"]);
        foreach (var child in configuration.GetSection("Controller:FallbackBootstrapUrls").GetChildren())
            AddBootstrapEndpoint(endpoints, child.Value);
        foreach (var value in (configuration["Controller:FallbackBootstrapUrls"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            AddBootstrapEndpoint(endpoints, value);
        return endpoints;
    }

    private static void AddBootstrapEndpoint(ICollection<Uri> endpoints, string? value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var bootstrap))
            AddEndpoint(endpoints, new Uri(bootstrap, "/api/core/agent/session/end").AbsoluteUri);
    }

    private static void AddEndpoint(ICollection<Uri> endpoints, string? value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var endpoint) && !endpoints.Any(existing => existing == endpoint))
            endpoints.Add(endpoint);
    }

    private static string? ReadString(string json, string name)
    {
        using var document = JsonDocument.Parse(json);
        return ReadString(document.RootElement, name);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}
