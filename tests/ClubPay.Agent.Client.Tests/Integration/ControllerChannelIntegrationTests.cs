using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Core.Services;
using ClubPay.Agent.TestHarness;

namespace ClubPay.Agent.Client.Tests.Integration;

/// <summary>
/// Drives the real ControllerChannelService + CommandDispatcherService + SessionCoordinatorService +
/// AgentStateRepository stack (wired through a real DI container, exactly like App.xaml.cs, so a
/// constructor-cycle regression would fail here too) against a FakeControllerServer over an actual
/// WebSocket loopback connection — verifying the wire format, idempotency and offline-outbox behavior
/// end to end, not just each piece in isolation.
/// </summary>
public sealed class ControllerChannelIntegrationTests : IAsyncDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "clubpay-agent-integration-" + Guid.NewGuid().ToString("N"));
    private readonly List<ServiceProvider> _providers = [];
    private readonly List<FakeControllerServer> _servers = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.GetRequiredService<IControllerChannel>().StopAsync();
            provider.Dispose();
        }

        foreach (var server in _servers)
            await server.DisposeAsync();

        if (Directory.Exists(_dataDir))
            Directory.Delete(_dataDir, recursive: true);
    }

    private async Task<(FakeControllerServer Server, IControllerChannel Channel)> StartFullStackAsync()
    {
        var server = new FakeControllerServer();
        _servers.Add(server);
        await server.StartAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Controller:WebSocketUrl"] = server.WebSocketUrl.ToString(),
                ["Controller:AgentToken"] = "test-token",
                ["Controller:ExternalPcId"] = "club12-pc07",
                ["Agent:DataDirectory"] = _dataDir,
            })
            .Build();

        var agent = new Mock<IAgentService>();
        agent.SetupGet(a => a.ExternalPcId).Returns("club12-pc07");

        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(config);
        sc.AddSingleton<AgentStateRepository>();
        sc.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<AgentStateRepository>());
        sc.AddSingleton<IGrantIdempotencyStore>(sp => sp.GetRequiredService<AgentStateRepository>());
        sc.AddSingleton<IControllerOutbox>(sp => sp.GetRequiredService<AgentStateRepository>());
        sc.AddSingleton(agent.Object);
        sc.AddSingleton(Mock.Of<IKioskLockService>());
        sc.AddSingleton(Mock.Of<IProcessCleanupService>());
        sc.AddSingleton(Mock.Of<IIdleDetectionService>());
        sc.AddSingleton<ISystemClock, SystemClock>();
        sc.AddSingleton(Mock.Of<IVoiceAnnouncementService>());
        sc.AddSingleton<ISessionCoordinator, SessionCoordinatorService>();
        sc.AddSingleton<ICommandDispatcher, CommandDispatcherService>();
        sc.AddSingleton<IControllerChannel, ControllerChannelService>();
        sc.AddLogging();

        var provider = sc.BuildServiceProvider();
        _providers.Add(provider);

        var channel = provider.GetRequiredService<IControllerChannel>();
        await channel.StartAsync();
        await server.WaitForConnectionAsync(TimeSpan.FromSeconds(5));

        return (server, channel);
    }

    [Fact]
    public async Task StartAsync_WhenServerAccepts_EmitsAgentOnlineEvent()
    {
        var (server, _) = await StartFullStackAsync();

        var evt = await server.AwaitNextEventAsync();

        Assert.Equal("agent_online", evt.Name);
    }

    [Fact]
    public async Task StartSession_RoundTrip_ReturnsOkAndPublishesSessionStarted()
    {
        var (server, _) = await StartFullStackAsync();
        await server.AwaitNextEventAsync(); // agent_online

        await server.SendCommandAsync("start_session", new
        {
            external_pc_id = "club12-pc07",
            grant_id = "grant_1001",
            granted_seconds = 3600,
            ends_at = DateTime.UtcNow.AddSeconds(3600),
            zone = "Standard",
            start_at = DateTime.UtcNow,
        }, commandId: "cmd_1");

        var result = await server.AwaitNextCommandResultAsync();
        Assert.Equal("cmd_1", result.CommandId);
        Assert.Equal("ok", result.Status);

        var evt = await server.AwaitNextEventAsync();
        Assert.Equal("session_started", evt.Name);
    }

    [Fact]
    public async Task StartSession_WhenGrantIdRepeated_ReturnsDuplicateWithoutNewSessionStartedEvent()
    {
        var (server, _) = await StartFullStackAsync();
        await server.AwaitNextEventAsync(); // agent_online

        var payload = new
        {
            external_pc_id = "club12-pc07",
            grant_id = "grant_dup",
            granted_seconds = 3600,
            ends_at = DateTime.UtcNow.AddSeconds(3600),
            zone = "Standard",
            start_at = DateTime.UtcNow,
        };

        await server.SendCommandAsync("start_session", payload, commandId: "cmd_1");
        await server.AwaitNextCommandResultAsync();
        await server.AwaitNextEventAsync(); // session_started
        await server.AwaitNextEventAsync(); // pc_state_changed

        await server.SendCommandAsync("start_session", payload, commandId: "cmd_2");
        var secondResult = await server.AwaitNextCommandResultAsync();

        Assert.Equal("ok", secondResult.Status);
        Assert.Equal("duplicate", secondResult.ErrorCode?.ToString().ToLowerInvariant());

        var timedOut = await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.AwaitNextEventAsync(TimeSpan.FromMilliseconds(300)));
        Assert.NotNull(timedOut);
    }

    [Fact]
    public async Task SimulateDisconnect_ThenReconnect_FlushesQueuedEventFromOutbox()
    {
        var (server, channel) = await StartFullStackAsync();
        await server.AwaitNextEventAsync(); // agent_online

        server.SimulateDisconnect();
        await channel.PublishEventAsync("time_low", new { core_session_id = "cs_1", remaining_seconds = 60, threshold = 60 });

        // time_low was queued into the outbox before the reconnect's own agent_online event, so it is
        // flushed first (FIFO) once the channel reconnects.
        var evt = await server.AwaitNextEventAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("time_low", evt.Name);
    }
}
