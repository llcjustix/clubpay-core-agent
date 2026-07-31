using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ClubPay.Agent.Admin.Services.Controller;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.TestHarness;

namespace ClubPay.Agent.Admin.Tests;

/// <summary>
/// End-to-end tests against the REAL ControllerHubService (real HttpListener + WebSocket) — the
/// counterpart of ClubPay.Agent.Client.Tests' ControllerChannelService integration tests, which
/// exercise the same v1.2 wire contract from the other side against FakeControllerServer. Here a
/// minimal fake agent (plain ClientWebSocket) stands in for a real ClubPay.Agent.Client instance.
/// </summary>
public sealed class ControllerHubServiceTests : IAsyncDisposable
{
    private const string ExternalPcId = "club12-pc01";
    private const string AgentToken = "test-token-123";

    private readonly int _port = TestPortAllocator.GetFreePort();
    private readonly IPcRegistry _registry;
    private readonly PcStateStore _stateStore;
    private readonly ControllerHubService _hub;

    public ControllerHubServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Controller:ListenPrefix"] = $"http://localhost:{_port}/",
                ["Controller:Pcs:0:ExternalPcId"] = ExternalPcId,
                ["Controller:Pcs:0:PcId"] = "PC-01",
                ["Controller:Pcs:0:Zone"] = "Standard",
                ["Controller:Pcs:0:AgentToken"] = AgentToken,
            })
            .Build();

        _registry = new PcRegistry(config);
        _stateStore = new PcStateStore(_registry, NullLogger<PcStateStore>.Instance);
        _hub = new ControllerHubService(
            config, _registry, _stateStore, new EventIdempotencyStore(), NullLogger<ControllerHubService>.Instance);
    }

    public async ValueTask DisposeAsync() => await _hub.DisposeAsync();

    [Fact]
    public async Task SendCommandAsync_NoAgentConnected_ReturnsAgentOffline()
    {
        await _hub.StartAsync();

        var result = await _hub.SendCommandAsync(
            ExternalPcId, "lock", new LockPayload(ExternalPcId, null), TimeSpan.FromSeconds(2));

        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.AgentOffline, result.ErrorCode);
    }

    [Fact]
    public async Task SendCommandAsync_AgentConnectedAndAcks_RoundTripsResult()
    {
        await _hub.StartAsync();
        await using var agent = await FakeAgent.ConnectAsync(_port, ExternalPcId, AgentToken);
        _ = agent.RunEchoLoopAsync();

        await WaitUntilConnectedAsync();

        var result = await _hub.SendCommandAsync(
            ExternalPcId, Constants.ControllerChannel.CommandName.Lock,
            new LockPayload(ExternalPcId, "manager"), TimeSpan.FromSeconds(5));

        Assert.Equal("ok", result.Status);
    }

    [Fact]
    public async Task SendCommandAsync_AgentNeverReplies_ReturnsCommandTimeout()
    {
        await _hub.StartAsync();
        await using var agent = await FakeAgent.ConnectAsync(_port, ExternalPcId, AgentToken);
        // no echo loop started — agent connects but never answers

        await WaitUntilConnectedAsync();

        var result = await _hub.SendCommandAsync(
            ExternalPcId, Constants.ControllerChannel.CommandName.Lock,
            new LockPayload(ExternalPcId, null), TimeSpan.FromMilliseconds(300));

        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.CommandTimeout, result.ErrorCode);
    }

    [Fact]
    public async Task Connect_WrongToken_IsRejected()
    {
        await _hub.StartAsync();

        await Assert.ThrowsAsync<WebSocketException>(
            () => FakeAgent.ConnectAsync(_port, ExternalPcId, "wrong-token"));
    }

    [Fact]
    public async Task AgentConnects_StateStoreReflectsConnectedAndFree()
    {
        await _hub.StartAsync();
        await using var agent = await FakeAgent.ConnectAsync(_port, ExternalPcId, AgentToken);

        await WaitUntilConnectedAsync();

        var state = _stateStore.Get(ExternalPcId)!;
        Assert.True(state.IsConnected);
        Assert.Equal(PcState.Free, state.PcState);
    }

    [Fact]
    public async Task AgentDisconnects_StateStoreMarksOffline()
    {
        await _hub.StartAsync();
        var agent = await FakeAgent.ConnectAsync(_port, ExternalPcId, AgentToken);
        await WaitUntilConnectedAsync();

        await agent.DisposeAsync();

        await WaitUntilAsync(() => _stateStore.Get(ExternalPcId)!.IsConnected == false);
        Assert.Equal(PcState.Offline, _stateStore.Get(ExternalPcId)!.PcState);
    }

    [Fact]
    public async Task ReceiveEvent_SendsEventAckWithMatchingEventId()
    {
        await _hub.StartAsync();
        await using var agent = await FakeAgent.ConnectAsync(_port, ExternalPcId, AgentToken);
        _ = agent.RunReceiveLoopAsync();
        await WaitUntilConnectedAsync();

        await agent.SendEventAsync(
            Constants.ControllerChannel.EventName.SessionStarted,
            new SessionStartedEvent("cs_1", ExternalPcId, "grant_1", null, DateTime.UtcNow),
            eventId: "ev_ack_test");

        var ack = await agent.AwaitNextEventAckAsync();
        Assert.Equal("ev_ack_test", ack.EventId);
    }

    [Fact]
    public async Task ReceiveDuplicateEvent_AppliesFirstPayloadOnly_AcksBoth()
    {
        await _hub.StartAsync();
        await using var agent = await FakeAgent.ConnectAsync(_port, ExternalPcId, AgentToken);
        _ = agent.RunReceiveLoopAsync();
        await WaitUntilConnectedAsync();

        await agent.SendEventAsync(
            Constants.ControllerChannel.EventName.SessionStarted,
            new SessionStartedEvent("cs_first", ExternalPcId, "grant_1", null, DateTime.UtcNow),
            eventId: "ev_dup");
        var firstAck = await agent.AwaitNextEventAckAsync();

        await agent.SendEventAsync(
            Constants.ControllerChannel.EventName.SessionStarted,
            new SessionStartedEvent("cs_second", ExternalPcId, "grant_1", null, DateTime.UtcNow),
            eventId: "ev_dup");
        var secondAck = await agent.AwaitNextEventAckAsync();

        Assert.Equal("ev_dup", firstAck.EventId);
        Assert.Equal("ev_dup", secondAck.EventId);
        Assert.Equal("cs_first", _stateStore.Get(ExternalPcId)!.CoreSessionId);
    }

    [Fact]
    public async Task ReceiveDuplicateEvent_ChangedFiresOnlyOnce()
    {
        await _hub.StartAsync();
        await using var agent = await FakeAgent.ConnectAsync(_port, ExternalPcId, AgentToken);
        _ = agent.RunReceiveLoopAsync();
        await WaitUntilConnectedAsync();

        var changeCount = 0;
        _stateStore.Changed += _ => changeCount++;

        await agent.SendEventAsync(
            Constants.ControllerChannel.EventName.SessionStarted,
            new SessionStartedEvent("cs_1", ExternalPcId, "grant_1", null, DateTime.UtcNow),
            eventId: "ev_changed_once");
        await agent.AwaitNextEventAckAsync();

        await agent.SendEventAsync(
            Constants.ControllerChannel.EventName.SessionStarted,
            new SessionStartedEvent("cs_1", ExternalPcId, "grant_1", null, DateTime.UtcNow),
            eventId: "ev_changed_once");
        await agent.AwaitNextEventAckAsync();

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public async Task ReceiveEvent_DifferentEventIds_BothAppliedAndAckedIndependently()
    {
        await _hub.StartAsync();
        await using var agent = await FakeAgent.ConnectAsync(_port, ExternalPcId, AgentToken);
        _ = agent.RunReceiveLoopAsync();
        await WaitUntilConnectedAsync();

        await agent.SendEventAsync(
            Constants.ControllerChannel.EventName.SessionStarted,
            new SessionStartedEvent("cs_1", ExternalPcId, "grant_1", null, DateTime.UtcNow),
            eventId: "ev_a");
        var ackA = await agent.AwaitNextEventAckAsync();

        await agent.SendEventAsync(
            Constants.ControllerChannel.EventName.SessionExtended,
            new SessionExtendedEvent("cs_1", ExternalPcId, "grant_2", 600, DateTime.UtcNow.AddMinutes(10)),
            eventId: "ev_b");
        var ackB = await agent.AwaitNextEventAckAsync();

        Assert.Equal("ev_a", ackA.EventId);
        Assert.Equal("ev_b", ackB.EventId);
    }

    private async Task WaitUntilConnectedAsync() =>
        await WaitUntilAsync(() => _stateStore.Get(ExternalPcId)?.IsConnected == true);

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition never became true");
            await Task.Delay(20);
        }
    }

    /// <summary>Minimal stand-in for ClubPay.Agent.Client.Services.ControllerChannelService's outbound
    /// connection — just enough wire behavior (connect, ack every command as "ok", send events, read
    /// event_acks) to exercise the hub.</summary>
    private sealed class FakeAgent : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket;
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly ConcurrentQueue<EventAckEnvelope> _acks = new();
        private readonly SemaphoreSlim _ackSignal = new(0);

        private FakeAgent(ClientWebSocket socket) => _socket = socket;

        public static async Task<FakeAgent> ConnectAsync(int port, string externalPcId, string token)
        {
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
            var uri = new Uri($"ws://localhost:{port}/agent/ws?external_pc_id={Uri.EscapeDataString(externalPcId)}");
            await socket.ConnectAsync(uri, CancellationToken.None);
            return new FakeAgent(socket);
        }

        /// <summary>Back-compat alias for the original command-only echo loop.</summary>
        public Task RunEchoLoopAsync() => RunReceiveLoopAsync();

        public Task RunReceiveLoopAsync(bool autoAckCommands = true) => Task.Run(async () =>
        {
            var buffer = new byte[8192];
            try
            {
                while (_socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                            return;
                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    ms.Position = 0;
                    using var doc = await JsonDocument.ParseAsync(ms, cancellationToken: _cts.Token);
                    var root = doc.RootElement;

                    switch (root.GetProperty("type").GetString())
                    {
                        case "command":
                            if (autoAckCommands)
                            {
                                var commandId = root.GetProperty("command_id").GetString()!;
                                var reply = new CommandResultEnvelope(
                                    Constants.ControllerChannel.MessageType.CommandResult, commandId, "ok", new EmptyResult());
                                await SendJsonAsync(reply, _cts.Token);
                            }
                            break;

                        case "event_ack":
                            var ack = root.Deserialize<EventAckEnvelope>(ControllerJsonOptions.Default);
                            if (ack is not null)
                            {
                                _acks.Enqueue(ack);
                                _ackSignal.Release();
                            }
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
        });

        public Task SendEventAsync(string name, object payload, string? eventId = null, CancellationToken ct = default)
        {
            var envelope = new EventEnvelope(
                "event", name, eventId ?? "ev_" + Guid.NewGuid().ToString("N"), DateTime.UtcNow, payload);
            return SendJsonAsync(envelope, ct);
        }

        public async Task<EventAckEnvelope> AwaitNextEventAckAsync(TimeSpan? timeout = null, CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
            await _ackSignal.WaitAsync(cts.Token);
            _acks.TryDequeue(out var ack);
            return ack!;
        }

        private async Task SendJsonAsync(object envelope, CancellationToken ct)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(envelope, ControllerJsonOptions.Default);
            await _sendLock.WaitAsync(ct);
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            // A graceful CloseAsync() would race with RunReceiveLoopAsync's own concurrent
            // ReceiveAsync on the same socket (only one outstanding receive is allowed) — Abort()
            // matches FakeControllerServer.SimulateDisconnect()'s hard-drop teardown pattern.
            _socket.Abort();
            _socket.Dispose();
            _sendLock.Dispose();
            _ackSignal.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
