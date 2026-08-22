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
    private readonly Uri? _endpoint;

    public ClientSessionEndService(
        ISessionCoordinator coordinator,
        IAgentService agent,
        IConfiguration configuration)
    {
        _coordinator = coordinator;
        _agent = agent;
        _agentToken = configuration["Controller:AgentToken"] ?? string.Empty;
        _endpoint = BuildEndpoint(configuration);
    }

    public async Task<ClientSessionEndResult> EndCurrentSessionAsync(
        string recipientPhone,
        bool recipientConsent,
        CancellationToken ct = default)
    {
        var session = _coordinator.CurrentSession
            ?? throw new InvalidOperationException("Active session was not found");
        if (session.CoreSessionId is not { } coreSessionId)
            throw new InvalidOperationException("Session is not linked to Core");
        if (_endpoint is null)
            throw new InvalidOperationException("Core session-end endpoint is not configured");

        var body = JsonSerializer.Serialize(new
        {
            external_pc_id = _agent.ExternalPcId,
            core_session_id = coreSessionId.ToString("N"),
            recipient_phone = recipientPhone,
            recipient_consent = recipientConsent,
        });
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _agentToken);

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var message = ReadString(responseBody, "error") ?? "Could not end the session";
            throw new InvalidOperationException(message);
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var voucher = root.TryGetProperty("voucher", out var voucherElement) ? voucherElement : default;
        var delivery = root.TryGetProperty("voucher_delivery", out var deliveryElement) ? deliveryElement : default;
        return new ClientSessionEndResult(
            VoucherCode: voucher.ValueKind == JsonValueKind.Object ? ReadString(voucher, "code") : null,
            VoucherSeconds: voucher.ValueKind == JsonValueKind.Object ? ReadInt(voucher, "seconds_left") : 0,
            DeliveryStatus: delivery.ValueKind == JsonValueKind.Object ? ReadString(delivery, "status") ?? "not_requested" : "not_requested",
            TelegramLink: delivery.ValueKind == JsonValueKind.Object ? ReadString(delivery, "telegram_link") : null);
    }

    private static Uri? BuildEndpoint(IConfiguration configuration)
    {
        var configured = configuration["Controller:SessionEndUrl"];
        if (Uri.TryCreate(configured, UriKind.Absolute, out var explicitUri))
            return explicitUri;

        if (!Uri.TryCreate(configuration["Controller:BootstrapUrl"], UriKind.Absolute, out var bootstrap))
            return null;
        return new Uri(bootstrap, "/api/core/agent/session/end");
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
