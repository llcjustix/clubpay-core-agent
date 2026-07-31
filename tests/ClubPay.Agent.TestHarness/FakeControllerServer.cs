using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core.Contracts;

namespace ClubPay.Agent.TestHarness;

/// <summary>
/// Minimal in-process mock Controller for verifying ClubPay.Agent.Client's outbound WebSocket channel
/// end to end — used by integration tests and by the standalone tools/MockController console app for
/// manual smoke-testing. Speaks the exact same wire format (command/command_result/event envelopes,
/// snake_case) as the real Controller would. Hosted on Kestrel (not HttpListener) so the ephemeral
/// port can be OS-assigned atomically via port 0 — no probe-then-bind race — and so shutdown honors
/// CancellationToken cleanly instead of relying on HttpListener.Stop()/Close() racing in-flight
/// native I/O.
/// </summary>
public sealed class FakeControllerServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<CommandResultEnvelope> _commandResults = new();
    private readonly ConcurrentQueue<EventEnvelope> _events = new();
    private readonly SemaphoreSlim _resultSignal = new(0);
    private readonly SemaphoreSlim _eventSignal = new(0);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, int> _eventDeliveryCounts = new();

    private WebSocket? _socket;

    public Uri WebSocketUrl { get; private set; } = null!;
    public string? LastAuthorizationHeader { get; private set; }
    public string? LastExternalPcId { get; private set; }
    public bool RejectNextConnection { get; set; }

    /// <summary>Default true so existing tests that don't care about the ack protocol need no changes.</summary>
    public bool AutoAckEvents { get; set; } = true;

    /// <summary>Delay applied before an auto-ack is sent — lets tests exercise the timeout/retry path.</summary>
    public TimeSpan? EventAckDelay { get; set; }

    /// <summary>Number of upcoming auto-acks to silently drop (decrements per suppressed event).</summary>
    public int SuppressNextEventAcks { get; set; }

    public int DeliveryCountFor(string eventId) => _eventDeliveryCounts.TryGetValue(eventId, out var c) ? c : 0;

    public FakeControllerServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

        _app = builder.Build();
        _app.UseWebSockets();
        _app.Map("/agent/ws", branch => branch.Run(HandleAgentWebSocketAsync));
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _app.StartAsync(ct);

        var boundAddress = new Uri(_app.Urls.First());
        WebSocketUrl = new Uri($"ws://{boundAddress.Host}:{boundAddress.Port}/agent/ws");
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts.Cancel();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await _app.StopAsync(timeoutCts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // best-effort shutdown — this is test infrastructure, not production code
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _app.DisposeAsync();
        _cts.Dispose();
        _resultSignal.Dispose();
        _eventSignal.Dispose();
        _sendLock.Dispose();
    }

    /// <summary>Sends a command to whichever agent is currently connected.</summary>
    public async Task SendCommandAsync(string name, object payload, string? commandId = null, CancellationToken ct = default)
    {
        await WaitForConnectionAsync(TimeSpan.FromSeconds(5), ct);
        var envelope = new CommandEnvelope(
            "command", name, commandId ?? "cmd_" + Guid.NewGuid().ToString("N"), DateTime.UtcNow, payload);
        await SendJsonAsync(envelope, ct);
    }

    /// <summary>Sends an event_ack for the given event_id to whichever agent is currently connected —
    /// for tests driving the ack protocol manually (AutoAckEvents = false).</summary>
    public Task SendEventAckAsync(string eventId, CancellationToken ct = default) =>
        SendJsonAsync(new EventAckEnvelope("event_ack", eventId), ct);

    private async Task SendJsonAsync(object envelope, CancellationToken ct)
    {
        if (_socket is not { State: WebSocketState.Open } socket)
            throw new InvalidOperationException("no agent is connected");

        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, ControllerJsonOptions.Default);
        await _sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task SendAckAfterOptionalDelayAsync(string eventId, CancellationToken ct)
    {
        try
        {
            if (EventAckDelay is { } delay)
                await Task.Delay(delay, ct);
            await SendEventAckAsync(eventId, ct);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // connection torn down (e.g. SimulateDisconnect) while the ack delay was pending — fine,
            // this is test infrastructure driving a deliberate disconnect-before-ack scenario
        }
        catch (InvalidOperationException)
        {
            // socket already gone by the time the delay elapsed — same benign case as above
        }
    }

    public async Task WaitForConnectionAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        while (_socket is not { State: WebSocketState.Open })
        {
            await Task.Delay(20, cts.Token);
        }
    }

    public async Task<CommandResultEnvelope> AwaitNextCommandResultAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
        await _resultSignal.WaitAsync(cts.Token);

        if (!_commandResults.TryDequeue(out var envelope))
            throw new InvalidOperationException("no command_result available");
        return envelope;
    }

    public async Task<EventEnvelope> AwaitNextEventAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
        await _eventSignal.WaitAsync(cts.Token);

        if (!_events.TryDequeue(out var envelope))
            throw new InvalidOperationException("no event available");
        return envelope;
    }

    /// <summary>Forcibly kills the connection without a close handshake, simulating a dropped network,
    /// so reconnect/outbox-flush behavior can be exercised.</summary>
    public void SimulateDisconnect()
    {
        try
        {
            _socket?.Abort();
        }
        catch (ObjectDisposedException)
        {
            // already gone — fine for a "make sure it's disconnected" test helper
        }
    }

    /// <summary>Invoked fresh by Kestrel for every new WebSocket upgrade to /agent/ws — including a
    /// reconnect after SimulateDisconnect — which is the direct equivalent of the old HttpListener
    /// accept loop's "go around again after each connection ends" behavior.</summary>
    private async Task HandleAgentWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        LastAuthorizationHeader = context.Request.Headers["Authorization"];
        LastExternalPcId = context.Request.Query["external_pc_id"];

        if (RejectNextConnection)
        {
            RejectNextConnection = false;
            context.Response.StatusCode = 401;
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, context.RequestAborted);
        _socket = await context.WebSockets.AcceptWebSocketAsync();
        await ReceiveLoopAsync(_socket, linkedCts.Token);
    }

    private async Task ReceiveLoopAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                ms.Position = 0;
                await HandleIncomingAsync(ms, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
            // connection dropped (e.g. SimulateDisconnect) — expected, caller will reconnect
        }
    }

    private async Task HandleIncomingAsync(Stream body, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            return;

        switch (typeProp.GetString())
        {
            case "command_result":
                var resultEnvelope = root.Deserialize<CommandResultEnvelope>(ControllerJsonOptions.Default);
                if (resultEnvelope is not null)
                {
                    _commandResults.Enqueue(resultEnvelope);
                    _resultSignal.Release();
                }
                break;

            case "event":
                var eventEnvelope = root.Deserialize<EventEnvelope>(ControllerJsonOptions.Default);
                if (eventEnvelope is not null)
                {
                    _events.Enqueue(eventEnvelope);
                    _eventSignal.Release();
                    _eventDeliveryCounts.AddOrUpdate(eventEnvelope.EventId, 1, (_, c) => c + 1);

                    if (AutoAckEvents)
                    {
                        if (SuppressNextEventAcks > 0)
                            SuppressNextEventAcks--;
                        else
                            _ = SendAckAfterOptionalDelayAsync(eventEnvelope.EventId, ct);
                    }
                }
                break;
        }
    }
}
