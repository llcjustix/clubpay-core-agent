using System.Net;
using System.Text.Json;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClubPay.Agent.Client.Tests.Services;

public sealed class LauncherCatalogServiceTests
{
    [Fact]
    public async Task SyncAsync_PostsToLocalAndCloud_AndUsesCloudManagerCategory()
    {
        var handler = new CatalogHandler();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Controller:AgentToken"] = "test-token",
                ["Controller:BootstrapUrl"] = "http://controller.club.local/api/core/bootstrap",
                ["Controller:FallbackBootstrapUrls:0"] = "https://api-clubpay.justix.uz/api/core/bootstrap",
            })
            .Build();
        var app = new LauncherApp("Counter-Strike 2", "C:\\Steam\\cs2.exe");
        var sut = new LauncherCatalogService(config, NullLogger<LauncherCatalogService>.Instance, handler);

        var categories = await sut.SyncAsync("pilot-real-network-pc-001", [app]);

        Assert.Equal(
            ["http://controller.club.local/api/core/launcher/catalog", "https://api-clubpay.justix.uz/api/core/launcher/catalog"],
            handler.RequestUris);
        Assert.Equal("shooter", categories[app.Key]);
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!.ToString());
            var payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(payload);
            var key = document.RootElement.GetProperty("apps")[0].GetProperty("key").GetString()!;
            var category = request.RequestUri.Host.Equals("api-clubpay.justix.uz", StringComparison.OrdinalIgnoreCase)
                ? "shooter"
                : "other";
            var response = JsonSerializer.Serialize(new { categories = new Dictionary<string, string> { [key] = category } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }
}
