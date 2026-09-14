using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Client.Services;

/// <summary>Synchronizes each PC's discovered launchers with centrally managed categories.</summary>
public sealed class LauncherCatalogService(IConfiguration config, ILogger<LauncherCatalogService> logger)
{
    private readonly IReadOnlyList<string> _bootstrapUrls = ReadUrls(config);
    private readonly string _agentToken = config["Controller:AgentToken"] ?? string.Empty;

    public async Task<IReadOnlyDictionary<string, string>> SyncAsync(
        string externalPcId, IReadOnlyCollection<LauncherApp> apps, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalPcId) || string.IsNullOrWhiteSpace(_agentToken) || _bootstrapUrls.Count == 0)
            return new Dictionary<string, string>();

        var payload = JsonSerializer.Serialize(new
        {
            external_pc_id = externalPcId,
            apps = apps.Select(app => new { key = app.Key, name = app.Name, exe_path = app.ExePath, args = app.Args, category = app.Category }),
        });
        foreach (var bootstrapUrl in _bootstrapUrls)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var request = new HttpRequestMessage(HttpMethod.Post, CatalogUri(bootstrapUrl))
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _agentToken);
                using var response = await client.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (!document.RootElement.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Object)
                    return new Dictionary<string, string>();
                return categories.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => LauncherCategories.Normalize(property.Value.GetString()),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new Dictionary<string, string>(); }
            catch (Exception ex) { logger.LogDebug(ex, "Could not synchronize launcher catalog with {Endpoint}", bootstrapUrl); }
        }
        return new Dictionary<string, string>();
    }

    private static Uri CatalogUri(string bootstrapEndpoint)
    {
        var builder = new UriBuilder(bootstrapEndpoint) { Path = "/api/core/launcher/catalog", Query = string.Empty };
        return builder.Uri;
    }

    private static IReadOnlyList<string> ReadUrls(IConfiguration configuration)
    {
        var urls = new List<string>();
        foreach (var value in new[] { configuration["Controller:BootstrapUrl"] }
            .Concat(configuration.GetSection("Controller:FallbackBootstrapUrls").GetChildren().Select(item => item.Value))
            .Concat((configuration["Controller:FallbackBootstrapUrls"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (!string.IsNullOrWhiteSpace(value) && !urls.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase)) urls.Add(value.Trim());
        }
        return urls;
    }
}
