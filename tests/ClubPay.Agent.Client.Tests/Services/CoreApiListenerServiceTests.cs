using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.Tests.Services;

public class CoreApiListenerServiceTests : IAsyncLifetime
{
    private const string ExternalPcId = "pc-001";
    private const string CoreToken = "test-token";

    private readonly Mock<ISessionStore> _store = new();
    private readonly Mock<IBillingEventReporter> _reporter = new();
    private readonly HttpClient _client;
    private readonly CoreApiListenerService _sut;

    public CoreApiListenerServiceTests()
    {
        var port = GetFreePort();

        var config = new Mock<IConfiguration>();
        config.Setup(c => c["Billing:ListenerPort"]).Returns(port.ToString());
        config.Setup(c => c["Billing:CoreToken"]).Returns(CoreToken);

        var agent = new Mock<IAgentService>();
        agent.Setup(a => a.ExternalPcId).Returns(ExternalPcId);

        _sut = new CoreApiListenerService(
            config.Object, agent.Object, _store.Object, _reporter.Object,
            NullLogger<CoreApiListenerService>.Instance);

        _client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}/") };
    }

    public async Task InitializeAsync() => await _sut.StartAsync();

    public async Task DisposeAsync()
    {
        await _sut.StopAsync();
        _client.Dispose();
    }

    [Fact]
    public async Task GetStatus_WhenExternalPcIdMatches_ReturnsOk()
    {
        _store.Setup(s => s.Current).Returns((Session?)null);

        var resp = await _client.GetAsync($"/core/v1/pcs/{ExternalPcId}/status");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetStatus_WhenExternalPcIdDoesNotMatch_ReturnsNotFound()
    {
        var resp = await _client.GetAsync("/core/v1/pcs/pc-999/status");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PostSessionsStart_WithoutBearerToken_ReturnsUnauthorized()
    {
        var resp = await _client.PostAsJsonAsync("/core/v1/sessions/start",
            new { external_pc_id = ExternalPcId, granted_seconds = 3600 });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task PostSessionsStart_WithValidPayload_FiresSessionStartReceivedAndReturnsOk()
    {
        CoreSessionStartCommand? received = null;
        _sut.SessionStartReceived += cmd => received = cmd;
        _client.DefaultRequestHeaders.Authorization = new("Bearer", CoreToken);

        var resp = await _client.PostAsJsonAsync("/core/v1/sessions/start", new
        {
            external_pc_id = ExternalPcId,
            granted_seconds = 3600,
            tariff_name = "1 soat",
            price_tiyin = 1_500_000
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(received);
        Assert.Equal(ExternalPcId, received!.ExternalPcId);
        Assert.Equal(3600, received.GrantedSeconds);
    }

    [Fact]
    public async Task PostSessionsStart_WithInvalidPayload_ReturnsBadRequestAndReportsCommandFailed()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", CoreToken);

        var resp = await _client.PostAsJsonAsync("/core/v1/sessions/start", new { external_pc_id = ExternalPcId });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        _reporter.Verify(r => r.ReportCommandFailedAsync("sessions/start", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PostSessionsExtend_WhenCoreSessionIdDoesNotMatchActiveSession_ReturnsNotFound()
    {
        _store.Setup(s => s.Current).Returns((Session?)null);
        _client.DefaultRequestHeaders.Authorization = new("Bearer", CoreToken);

        var resp = await _client.PostAsJsonAsync($"/core/v1/sessions/{Guid.NewGuid()}/extend",
            new { additional_seconds = 600 });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task PostSessionsExtend_WhenCoreSessionIdMatchesActiveSession_FiresSessionExtendReceived()
    {
        var coreSessionId = Guid.NewGuid();
        var session = new Session(Guid.NewGuid(), "PC-12",
            new Tariff(Guid.NewGuid(), "1 soat", ZoneType.Standard, 60, 1_500_000),
            DateTime.UtcNow, 3600, coreSessionId);
        _store.Setup(s => s.Current).Returns(session);

        CoreSessionExtendCommand? received = null;
        _sut.SessionExtendReceived += cmd => received = cmd;
        _client.DefaultRequestHeaders.Authorization = new("Bearer", CoreToken);

        var resp = await _client.PostAsJsonAsync($"/core/v1/sessions/{coreSessionId}/extend",
            new { additional_seconds = 600 });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.NotNull(received);
        Assert.Equal(600, received!.AdditionalSeconds);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
