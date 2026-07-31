using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ClubPay.Agent.Client.Services;
using ClubPay.Agent.Core.Contracts.Enums;
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

    private async Task<(FakeControllerServer Server, IControllerChannel Channel)> StartFullStackAsync(
        IDictionary<string, string?>? extraConfig = null)
    {
        var server = new FakeControllerServer();
        _servers.Add(server);
        await server.StartAsync();

        var configValues = new Dictionary<string, string?>
        {
            ["Controller:WebSocketUrl"] = server.WebSocketUrl.ToString(),
            ["Controller:AgentToken"] = "test-token",
            ["Controller:ExternalPcId"] = "club12-pc07",
            ["Agent:DataDirectory"] = _dataDir,
        };
        if (extraConfig is not null)
            foreach (var (key, value) in extraConfig)
                configValues[key] = value;

        var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        var agent = new Mock<IAgentService>();
        agent.SetupGet(a => a.ExternalPcId).Returns("club12-pc07");

        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(config);
        sc.AddSingleton<AgentStateRepository>();
        sc.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<AgentStateRepository>());
        sc.AddSingleton<IGrantIdempotencyStore>(sp => sp.GetRequiredService<AgentStateRepository>());
        sc.AddSingleton<IControllerOutbox>(sp => sp.GetRequiredService<AgentStateRepository>());
        sc.AddSingleton<ICommandIdempotencyStore>(sp => sp.GetRequiredService<AgentStateRepository>());
        sc.AddSingleton(agent.Object);
        sc.AddSingleton(Mock.Of<IKioskLockService>());
        sc.AddSingleton(Mock.Of<IProcessCleanupService>());
        sc.AddSingleton(Mock.Of<IIdleDetectionService>());
        sc.AddSingleton<ISystemClock, SystemClock>();
        sc.AddSingleton<ISessionCoordinator, SessionCoordinatorService>();
        sc.AddSingleton<ICommandValidator, CommandValidationService>();
        sc.AddSingleton<ICommandDispatcher, CommandDispatcherService>();
        sc.AddSingleton<IControllerChannel, ControllerChannelService>();
        sc.AddSingleton<IConnectionStateProvider>(sp => sp.GetRequiredService<IControllerChannel>());
        sc.AddLogging();

        var provider = sc.BuildServiceProvider();
        _providers.Add(provider);

        var channel = provider.GetRequiredService<IControllerChannel>();
        channel.IncomingCommandHandler = provider.GetRequiredService<ICommandDispatcher>().DispatchAsync;
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
    public async Task StartSession_WithMismatchedExternalPcId_ReturnsErrorAndCommandFailedEvent()
    {
        var (server, _) = await StartFullStackAsync();
        await server.AwaitNextEventAsync(); // agent_online

        await server.SendCommandAsync("start_session", new
        {
            external_pc_id = "club12-pc99",
            grant_id = "grant_1001",
            granted_seconds = 3600,
            ends_at = DateTime.UtcNow.AddSeconds(3600),
            zone = "Standard",
            start_at = DateTime.UtcNow,
        }, commandId: "cmd_1");

        var result = await server.AwaitNextCommandResultAsync();
        Assert.Equal("cmd_1", result.CommandId);
        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.InvalidState, result.ErrorCode);

        var evt = await server.AwaitNextEventAsync();
        Assert.Equal("command_failed", evt.Name);
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
    public async Task EndSession_WhenCommandIdRepeated_ReturnsCachedResultWithoutSecondSessionEndedEvent()
    {
        var (server, _) = await StartFullStackAsync();
        await server.AwaitNextEventAsync(); // agent_online

        await server.SendCommandAsync("start_session", new
        {
            external_pc_id = "club12-pc07",
            grant_id = "grant_end_1",
            granted_seconds = 3600,
            ends_at = DateTime.UtcNow.AddSeconds(3600),
            zone = "Standard",
            start_at = DateTime.UtcNow,
        }, commandId: "cmd_start");
        var startResult = await server.AwaitNextCommandResultAsync();
        await server.AwaitNextEventAsync(); // session_started
        await server.AwaitNextEventAsync(); // pc_state_changed

        var coreSessionId = ((JsonElement)startResult.Payload!).GetProperty("core_session_id").GetString();
        var endPayload = new { core_session_id = coreSessionId, reason = "MANAGER" };

        await server.SendCommandAsync("end_session", endPayload, commandId: "cmd_end_1");
        var firstEnd = await server.AwaitNextCommandResultAsync();
        Assert.Equal("ok", firstEnd.Status);
        await server.AwaitNextEventAsync(); // session_ended
        await server.AwaitNextEventAsync(); // pc_state_changed

        await server.SendCommandAsync("end_session", endPayload, commandId: "cmd_end_1");
        var secondEnd = await server.AwaitNextCommandResultAsync();

        Assert.Equal("ok", secondEnd.Status);

        var timedOut = await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.AwaitNextEventAsync(TimeSpan.FromMilliseconds(300)));
        Assert.NotNull(timedOut);
    }

    [Fact]
    public async Task Lock_WhenCommandIdRepeated_ReturnsCachedResultAndDoesNotReemitPcStateChanged()
    {
        var (server, _) = await StartFullStackAsync();
        await server.AwaitNextEventAsync(); // agent_online

        var payload = new { external_pc_id = "club12-pc07", reason = "manager" };

        await server.SendCommandAsync("lock", payload, commandId: "cmd_lock_1");
        var firstResult = await server.AwaitNextCommandResultAsync();
        Assert.Equal("ok", firstResult.Status);
        await server.AwaitNextEventAsync(); // pc_state_changed

        await server.SendCommandAsync("lock", payload, commandId: "cmd_lock_1");
        var secondResult = await server.AwaitNextCommandResultAsync();

        Assert.Equal("ok", secondResult.Status);

        var timedOut = await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.AwaitNextEventAsync(TimeSpan.FromMilliseconds(300)));
        Assert.NotNull(timedOut);
    }

    [Fact]
    public async Task SetRepair_WhenCommandIdRepeatedWithDifferentPayload_ReturnsConflict()
    {
        var (server, _) = await StartFullStackAsync();
        await server.AwaitNextEventAsync(); // agent_online

        await server.SendCommandAsync("set_repair", new { external_pc_id = "club12-pc07", on = true }, commandId: "cmd_repair_1");
        var firstResult = await server.AwaitNextCommandResultAsync();
        Assert.Equal("ok", firstResult.Status);
        await server.AwaitNextEventAsync(); // pc_state_changed

        await server.SendCommandAsync("set_repair", new { external_pc_id = "club12-pc07", on = false }, commandId: "cmd_repair_1");
        var secondResult = await server.AwaitNextCommandResultAsync();

        Assert.Equal("error", secondResult.Status);
        Assert.Equal("conflict", secondResult.ErrorCode?.ToString().ToLowerInvariant());

        var failedEvent = await server.AwaitNextEventAsync();
        Assert.Equal("command_failed", failedEvent.Name);
    }

    [Fact]
    public async Task Restart_ThenReplayCommandId_ReturnsCachedResultWithoutReexecuting()
    {
        var (server1, _) = await StartFullStackAsync();
        await server1.AwaitNextEventAsync(); // agent_online

        var payload = new { external_pc_id = "club12-pc07", reason = "manager" };
        await server1.SendCommandAsync("lock", payload, commandId: "cmd_restart_1");
        var firstResult = await server1.AwaitNextCommandResultAsync();
        await server1.AwaitNextEventAsync(); // pc_state_changed

        var (server2, _) = await StartFullStackAsync(); // simulates a process restart against the same _dataDir
        await server2.AwaitNextEventAsync(); // agent_online

        await server2.SendCommandAsync("lock", payload, commandId: "cmd_restart_1");
        var secondResult = await server2.AwaitNextCommandResultAsync();

        Assert.Equal(firstResult.Status, secondResult.Status);
        Assert.Equal(firstResult.CommandId, secondResult.CommandId);

        var timedOut = await Assert.ThrowsAsync<OperationCanceledException>(
            () => server2.AwaitNextEventAsync(TimeSpan.FromMilliseconds(300)));
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
        Assert.True(server.DeliveryCountFor(evt.EventId) >= 1);
    }

    [Fact]
    public async Task PublishDurableEvent_Acked_NotRedeliveredAfterReconnect()
    {
        var (server, channel) = await StartFullStackAsync();
        await server.AwaitNextEventAsync(); // agent_online
        var outbox = _providers[^1].GetRequiredService<IControllerOutbox>();

        await channel.PublishEventAsync("time_low", new { core_session_id = "cs_1", remaining_seconds = 60, threshold = 60 });
        var evt = await server.AwaitNextEventAsync();
        Assert.Equal("time_low", evt.Name);

        // Default AutoAckEvents already sent the ack — wait for the outbox row to actually be removed
        // (deletion is ack-gated now, not send-gated) before proving it's gone for good.
        await WaitUntilOutboxEmptyAsync(outbox, TimeSpan.FromSeconds(5));

        server.SimulateDisconnect();

        // Only a fresh agent_online should show up post-reconnect — time_low is already acked/deleted,
        // so it must not be redelivered.
        var reconnectEvt = await server.AwaitNextEventAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("agent_online", reconnectEvt.Name);

        var timedOut = await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.AwaitNextEventAsync(TimeSpan.FromMilliseconds(300)));
        Assert.NotNull(timedOut);
    }

    [Fact]
    public async Task PublishDurableEvent_DisconnectBeforeAckArrives_ResentWithSameEventIdAfterReconnect()
    {
        var (server, channel) = await StartFullStackAsync(new Dictionary<string, string?>
        {
            ["Controller:EventAckTimeoutSeconds"] = "1",
        });
        await server.AwaitNextEventAsync(); // agent_online
        await Task.Delay(200); // let agent_online's own auto-ack complete before we disable acking

        server.AutoAckEvents = false;
        await channel.PublishEventAsync("time_low", new { core_session_id = "cs_1", remaining_seconds = 60, threshold = 60 });
        var evt = await server.AwaitNextEventAsync();
        Assert.Equal("time_low", evt.Name);

        server.SimulateDisconnect();
        server.AutoAckEvents = true; // the controller "comes back" and starts acking normally again

        // time_low has the lower outbox sequence (queued before the reconnect's own agent_online), so
        // FIFO delivers it first, with the SAME event_id it was originally published with.
        var resent = await server.AwaitNextEventAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("time_low", resent.Name);
        Assert.Equal(evt.EventId, resent.EventId);

        var agentOnlineAfterReconnect = await server.AwaitNextEventAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("agent_online", agentOnlineAfterReconnect.Name);

        Assert.True(server.DeliveryCountFor(evt.EventId) >= 2);
    }

    [Fact]
    public async Task PublishDurableEvent_AckNeverArrives_RetriesUpToMaxThenReconnects()
    {
        var (server, channel) = await StartFullStackAsync(new Dictionary<string, string?>
        {
            ["Controller:EventAckTimeoutSeconds"] = "1",
            ["Controller:MaxEventSendRetries"] = "2",
        });
        await server.AwaitNextEventAsync(); // agent_online
        await Task.Delay(200); // let agent_online's own auto-ack complete before we disable acking

        var stateChanges = new List<ChannelConnectionState>();
        channel.ConnectionStateChanged += s => stateChanges.Add(s);

        server.AutoAckEvents = false;
        await channel.PublishEventAsync("time_low", new { core_session_id = "cs_1", remaining_seconds = 60, threshold = 60 });
        var evt = await server.AwaitNextEventAsync();
        Assert.Equal("time_low", evt.Name);

        // 2 retries * 1s ack timeout ≈ 2s before the connection tears down and reconnects; give it
        // enough headroom to cycle through at least once more.
        await Task.Delay(TimeSpan.FromSeconds(5));

        Assert.Contains(ChannelConnectionState.Reconnecting, stateChanges);
        Assert.True(
            server.DeliveryCountFor(evt.EventId) >= 2,
            $"expected at least 2 delivery attempts, got {server.DeliveryCountFor(evt.EventId)}");
    }

    [Fact]
    public async Task PublishDurableEvent_LateAckWithinTimeout_NoDuplicateSend()
    {
        var (server, channel) = await StartFullStackAsync(new Dictionary<string, string?>
        {
            ["Controller:EventAckTimeoutSeconds"] = "3",
        });
        await server.AwaitNextEventAsync(); // agent_online
        await Task.Delay(200);

        server.EventAckDelay = TimeSpan.FromSeconds(1); // arrives late, but well within the 3s timeout

        await channel.PublishEventAsync("time_low", new { core_session_id = "cs_1", remaining_seconds = 60, threshold = 60 });
        var evt = await server.AwaitNextEventAsync();
        Assert.Equal("time_low", evt.Name);

        await Task.Delay(TimeSpan.FromSeconds(2)); // ack should have long arrived by now

        Assert.Equal(1, server.DeliveryCountFor(evt.EventId));
    }

    [Fact]
    public async Task MultipleDurableEventsPending_DeliveredOneAtATimeInOrder()
    {
        var (server, channel) = await StartFullStackAsync();
        await server.AwaitNextEventAsync(); // agent_online
        await Task.Delay(200);

        server.AutoAckEvents = false; // hold delivery so we can pace acks manually

        await channel.PublishEventAsync("time_low", new { core_session_id = "cs_1", remaining_seconds = 300, threshold = 300 });
        await channel.PublishEventAsync("time_low", new { core_session_id = "cs_1", remaining_seconds = 60, threshold = 60 });

        var first = await server.AwaitNextEventAsync(TimeSpan.FromSeconds(3));

        // The second event must not arrive yet — stop-and-wait blocks it behind the first's unacked send.
        var timedOut = await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.AwaitNextEventAsync(TimeSpan.FromMilliseconds(500)));
        Assert.NotNull(timedOut);

        await server.SendEventAckAsync(first.EventId);
        var second = await server.AwaitNextEventAsync(TimeSpan.FromSeconds(3));

        Assert.NotEqual(first.EventId, second.EventId);
        Assert.Equal(300, ((JsonElement)first.Payload).GetProperty("remaining_seconds").GetInt32());
        Assert.Equal(60, ((JsonElement)second.Payload).GetProperty("remaining_seconds").GetInt32());
    }

    [Fact]
    public async Task PublishTelemetryEvent_NeverBlockedByMissingAck()
    {
        var (server, channel) = await StartFullStackAsync();
        await server.AwaitNextEventAsync(); // agent_online
        await Task.Delay(200);

        server.AutoAckEvents = false; // a durable event would now stall forever waiting for an ack

        await channel.PublishEventAsync("heartbeat", new
        {
            external_pc_id = "club12-pc07",
            pc_state = "Free",
            controllers_seen = 0,
            server_reachable = true,
        });

        var evt = await server.AwaitNextEventAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("heartbeat", evt.Name);
    }

    private static async Task WaitUntilOutboxEmptyAsync(IControllerOutbox outbox, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if ((await outbox.GetPendingAsync()).Count == 0)
                return;
            await Task.Delay(20);
        }

        throw new TimeoutException("outbox never drained");
    }
}
