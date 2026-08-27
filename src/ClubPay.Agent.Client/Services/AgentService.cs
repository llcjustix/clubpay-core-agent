using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

public sealed class AgentService : IAgentService
{
    public string PcId { get; private set; }
    public string ExternalPcId { get; }
    public ZoneType Zone { get; }
    public string ZoneName { get; private set; }
    public string ClubName { get; private set; }
    public string TimeZoneId { get; private set; }
    public string WifiSsid { get; }
    public string WifiPassword { get; }
    public string? StaticPaymentQrUrl { get; private set; }
    public event Action? StaticPaymentQrUrlChanged;
    public event Action? BootstrapChanged;

    private readonly string _bootstrapUrl;
    private readonly string _agentToken;
    private readonly ILogger<AgentService> _logger;

    public AgentService(IConfiguration config, ILogger<AgentService> logger)
    {
        _logger = logger;
        PcId = config["Agent:PcId"] ?? "PC-01";
        ClubName = config["Agent:ClubName"] ?? "NEXUS ARENA";
        TimeZoneId = config["Agent:TimeZone"] ?? "Asia/Tashkent";
        WifiSsid = config["Agent:WifiSsid"] ?? "ClubPay-Guest";
        WifiPassword = config["Agent:WifiPassword"] ?? string.Empty;
        Zone = Enum.TryParse<ZoneType>(config["Agent:Zone"], out var z) ? z : ZoneType.Standard;
        ZoneName = config["Agent:ZoneName"] ?? Zone.ToString();

        var externalPcId = config["Controller:ExternalPcId"];
        if (string.IsNullOrWhiteSpace(externalPcId))
        {
            logger.LogWarning("Controller:ExternalPcId is not configured — falling back to lowercased PcId");
            externalPcId = PcId.ToLowerInvariant();
        }
        ExternalPcId = externalPcId;
        _bootstrapUrl = config["Controller:BootstrapUrl"] ?? string.Empty;
        _agentToken = config["Controller:AgentToken"] ?? string.Empty;
        StaticPaymentQrUrl = config["Qr:StaticQrUrl"];
    }

    public async Task RefreshStaticPaymentQrUrlAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_bootstrapUrl))
        {
            _logger.LogWarning("Controller:BootstrapUrl is not configured; using the local static QR fallback if present");
            return;
        }

        try
        {
            var bootstrapUri = BuildBootstrapUri();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, bootstrapUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _agentToken);
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!payload.RootElement.TryGetProperty("qr_url", out var qrUrlProperty) ||
                string.IsNullOrWhiteSpace(qrUrlProperty.GetString()))
            {
                throw new InvalidOperationException("Core bootstrap response does not contain qr_url");
            }

            var qrUrl = qrUrlProperty.GetString()!;
            if (!Uri.TryCreate(qrUrl, UriKind.Absolute, out _))
                throw new InvalidOperationException("Core bootstrap returned an invalid qr_url");

            var bootstrapChanged = ApplyBootstrapIdentity(payload.RootElement);
            if (!string.Equals(StaticPaymentQrUrl, qrUrl, StringComparison.Ordinal))
            {
                StaticPaymentQrUrl = qrUrl;
                StaticPaymentQrUrlChanged?.Invoke();
            }
            if (bootstrapChanged)
                BootstrapChanged?.Invoke();

            _logger.LogInformation("Static payment QR loaded from Core bootstrap for {ExternalPcId}", ExternalPcId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load static payment QR from Core bootstrap for {ExternalPcId}; retaining fallback if configured", ExternalPcId);
        }
    }

    private bool ApplyBootstrapIdentity(JsonElement payload)
    {
        var changed = false;
        var clubName = ReadNestedString(payload, "club", "name");
        if (!string.IsNullOrWhiteSpace(clubName) && !string.Equals(ClubName, clubName, StringComparison.Ordinal))
        {
            ClubName = clubName;
            changed = true;
        }
        var pcLabel = ReadNestedString(payload, "pc", "label");
        if (!string.IsNullOrWhiteSpace(pcLabel) && !string.Equals(PcId, pcLabel, StringComparison.Ordinal))
        {
            PcId = pcLabel;
            changed = true;
        }
        var zoneName = ReadNestedString(payload, "zone", "name");
        if (!string.IsNullOrWhiteSpace(zoneName) && !string.Equals(ZoneName, zoneName, StringComparison.Ordinal))
        {
            ZoneName = zoneName;
            changed = true;
        }
        var timeZoneId = ReadNestedString(payload, "club", "timezone");
        if (!string.IsNullOrWhiteSpace(timeZoneId) && !string.Equals(TimeZoneId, timeZoneId, StringComparison.Ordinal))
        {
            TimeZoneId = timeZoneId;
            changed = true;
        }
        return changed;
    }

    private static string? ReadNestedString(JsonElement payload, string objectName, string propertyName)
        => payload.TryGetProperty(objectName, out var container)
            && container.ValueKind == JsonValueKind.Object
            && container.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private Uri BuildBootstrapUri()
    {
        var builder = new UriBuilder(_bootstrapUrl);
        var existingQuery = builder.Query.TrimStart('?');
        var pcIdQuery = $"external_pc_id={Uri.EscapeDataString(ExternalPcId)}";
        builder.Query = string.IsNullOrEmpty(existingQuery) ? pcIdQuery : $"{existingQuery}&{pcIdQuery}";
        return builder.Uri;
    }

    public Task SleepAsync(CancellationToken ct = default)
    {
        NativeMethods.SetSuspendState(false, false, false);
        return Task.CompletedTask;
    }

    public void KeepAwake(bool keepAwake)
    {
        NativeMethods.SetThreadExecutionState(keepAwake
            ? NativeMethods.ExecutionState.Continuous | NativeMethods.ExecutionState.SystemRequired
            : NativeMethods.ExecutionState.Continuous);
    }
}

internal static class NativeMethods
{
    [Flags]
    internal enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
    }

    [System.Runtime.InteropServices.DllImport("powrprof.dll", SetLastError = true)]
    internal static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    internal static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);
}
