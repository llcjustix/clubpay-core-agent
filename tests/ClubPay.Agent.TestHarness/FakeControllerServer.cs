using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using ClubPay.Agent.Core.Contracts;

namespace ClubPay.Agent.TestHarness;

/// <summary>
/// Minimal in-process mock Controller for verifying ClubPay.Agent.Client's outbound WebSocket channel
/// end to end — used by integration tests and by the standalone tools/MockController console app for
/// manual smoke-testing. Speaks the exact same wire format (command/command_result/event envelopes,
/// snake_case) as the real Controller would.
/// </summary>
public sealed class FakeControllerServer : IAsyncDisposable
{
    private readonly HttpListener _http = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<CommandResultEnvelope> _commandResults = new();
    private readonly ConcurrentQueue<EventEnvelope> _events = new();
    private readonly SemaphoreSlim _resultSignal = new(0);
    private readonly SemaphoreSlim _eventSignal = new(0);

    private WebSocket? _socket;
    private Task? _acceptTask;

    public Uri WebSocketUrl { get; }
    public string? LastAuthorizationHeader { get; private set; }
    public string? LastExternalPcId { get; private set; }
    public bool RejectNextConnection { get; set; }

    public FakeControllerServer()
    {
        int port = GetFreePort();
        WebSocketUrl = new Uri($"ws://localhost:{port}/agent/ws");
        _http.Prefixes.Add($"http://localhost:{port}/");
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _http.Start();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts.Cancel();
        _http.Stop();

        if (_acceptTask is null)
            return;

        try
        {
            await _acceptTask.WaitAsync(TimeSpan.FromSeconds(2), ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // best-effort shutdown — this is test infrastructure, not production code
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _http.Close();
        _cts.Dispose();
        _resultSignal.Dispose();
        _eventSignal.Dispose();
    }

    /// <summary>Sends a command to whichever agent is currently connected.</summary>
    public async Task SendCommandAsync(string name, object payload, string? commandId = null, CancellationToken ct = default)
    {
        await WaitForConnectionAsync(TimeSpan.FromSeconds(5), ct);
        if (_socket is not { State: WebSocketState.Open } socket)
            throw new InvalidOperationException("no agent is connected");

        var envelope = new CommandEnvelope(
            "command", name, commandId ?? "cmd_" + Guid.NewGuid().ToString("N"), DateTime.UtcNow, payload);
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, ControllerJsonOptions.Default);
        await socket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, endOfMessage: true, ct);
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

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var context = await _http.GetContextAsync();

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                LastAuthorizationHeader = context.Request.Headers["Authorization"];
                LastExternalPcId = context.Request.QueryString["external_pc_id"];

                if (RejectNextConnection)
                {
                    RejectNextConnection = false;
                    context.Response.StatusCode = 401;
                    context.Response.Close();
                    continue;
                }

                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                _socket = wsContext.WebSocket;
                await ReceiveLoopAsync(_socket, ct);
            }
        }
        catch (HttpListenerException)
        {
            // listener stopped — normal on StopAsync
        }
        catch (ObjectDisposedException)
        {
            // listener disposed — normal on StopAsync
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
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
                }
                break;
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
}
