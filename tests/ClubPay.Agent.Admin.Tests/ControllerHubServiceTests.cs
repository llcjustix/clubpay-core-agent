using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ClubPay.Agent.Admin.Services.Controller;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Payloads;

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

    private readonly int _port = GetFreePort();
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
        _hub = new ControllerHubService(config, _registry, _stateStore, NullLogger<ControllerHubService>.Instance);
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

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Minimal stand-in for ClubPay.Agent.Client.Services.ControllerChannelService's outbound
    /// connection — just enough wire behavior (connect, ack every command as "ok") to exercise the hub.</summary>
    private sealed class FakeAgent : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket;
        private readonly CancellationTokenSource _cts = new();

        private FakeAgent(ClientWebSocket socket) => _socket = socket;

        public static async Task<FakeAgent> ConnectAsync(int port, string externalPcId, string token)
        {
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
            var uri = new Uri($"ws://localhost:{port}/agent/ws?external_pc_id={Uri.EscapeDataString(externalPcId)}");
            await socket.ConnectAsync(uri, CancellationToken.None);
            return new FakeAgent(socket);
        }

        public Task RunEchoLoopAsync() => Task.Run(async () =>
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
                    var commandId = root.GetProperty("command_id").GetString()!;

                    var reply = new CommandResultEnvelope(
                        Constants.ControllerChannel.MessageType.CommandResult, commandId, "ok", new EmptyResult());
                    var json = JsonSerializer.SerializeToUtf8Bytes(reply, ControllerJsonOptions.Default);
                    await _socket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
        });

        public ValueTask DisposeAsync()
        {
            _cts.Cancel();
            // A graceful CloseAsync() would race with RunEchoLoopAsync's own concurrent
            // ReceiveAsync on the same socket (only one outstanding receive is allowed) — Abort()
            // matches FakeControllerServer.SimulateDisconnect()'s hard-drop teardown pattern.
            _socket.Abort();
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
