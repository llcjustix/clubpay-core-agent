using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Payloads;

namespace ClubPay.Agent.Admin.Services.Controller;

/// <summary>
/// The Admin's controller-engine: a multi-connection WebSocket server that Agents dial into,
/// speaking the exact same v1.2 wire contract (command/command_result/event, snake_case) that
/// <c>ControllerChannelService</c> on the Client side already implements — see
/// <c>tests/ClubPay.Agent.TestHarness/FakeControllerServer.cs</c> for the single-connection
/// precursor this generalizes. This is what makes Admin the ТЗ §9 "manager PC" T2-authority
/// instead of a disconnected demo UI.
/// </summary>
public sealed class ControllerHubService : IAsyncDisposable
{
    private const int StatusPollIntervalSeconds = 15;
    private const int StatusPollTimeoutSeconds = 5;
    private const int MaxStartAttempts = 3;

    private sealed class PcConnection
    {
        public required WebSocket Socket { get; init; }
        public required string ExternalPcId { get; init; }
        public SemaphoreSlim CommandGate { get; } = new(1, 1);

        // A WebSocket only tolerates one outstanding SendAsync at a time — SendCommandAsync (manager
        // action / status poll) and the event_ack reply from the receive loop can fire concurrently on
        // the same connection, so every raw send goes through this lock.
        public SemaphoreSlim SendLock { get; } = new(1, 1);
        public TaskCompletionSource<CommandResultEnvelope>? PendingResult { get; set; }
        public string? PendingCommandId { get; set; }
    }

    private readonly HttpListener _http = new();
    private readonly IPcRegistry _registry;
    private readonly IPcStateStore _stateStore;
    private readonly IEventIdempotencyStore _eventIdempotency;
    private readonly ILogger<ControllerHubService> _logger;
    private readonly ConcurrentDictionary<string, PcConnection> _connections = new();
    private readonly ConcurrentDictionary<Task, byte> _connectionTasks = new();

    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private Task? _pollTask;

    public ControllerHubService(
        IConfiguration config,
        IPcRegistry registry,
        IPcStateStore stateStore,
        IEventIdempotencyStore eventIdempotency,
        ILogger<ControllerHubService> logger)
    {
        _registry = registry;
        _stateStore = stateStore;
        _eventIdempotency = eventIdempotency;
        _logger = logger;

        var prefix = config["Controller:ListenPrefix"] ?? "http://+:8787/";
        _http.Prefixes.Add(prefix);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _http.Start();
                break;
            }
            catch (HttpListenerException ex) when (attempt < MaxStartAttempts)
            {
                // A previous listener on the same configured port may still be tearing down
                // (TIME_WAIT / handle-close lag) — short backoff and retry on the same port, since
                // the port is operator-configured, not something this service can renegotiate.
                _logger.LogWarning(ex, "ControllerHub portini bog'lashda vaqtinchalik xato, qayta urinilmoqda ({Attempt}/{Max})", attempt, MaxStartAttempts);
                Thread.Sleep(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }

        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
        _pollTask = Task.Run(() => StatusPollLoopAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("ControllerHub ishga tushdi: {Prefix}", _http.Prefixes.First());
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        _http.Stop();

        var tasks = new[] { _acceptTask, _pollTask }
            .Where(t => t is not null)
            .Select(t => t!)
            .Concat(_connectionTasks.Keys);
        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            _logger.LogWarning(ex, "ControllerHub'ni yopishda kutish tugadi");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _http.Close();
        _cts?.Dispose();
    }

    /// <summary>Sends a command to the given PC and awaits its synchronous ack (contract §1):
    /// AgentOffline immediately if not connected, CommandTimeout if no reply within <paramref name="timeout"/>.</summary>
    public async Task<CommandResultEnvelope> SendCommandAsync(
        string externalPcId, string name, object payload, TimeSpan timeout, CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(externalPcId, out var connection))
            return OfflineResult(name);

        await connection.CommandGate.WaitAsync(ct);
        try
        {
            var commandId = "cmd_" + Guid.NewGuid().ToString("N")[..12];
            var envelope = new CommandEnvelope(
                Constants.ControllerChannel.MessageType.Command, name, commandId, DateTime.UtcNow, payload);

            var tcs = new TaskCompletionSource<CommandResultEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.PendingCommandId = commandId;
            connection.PendingResult = tcs;

            await SendJsonAsync(connection, envelope, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                return await tcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Command timeout: {Name} ({ExternalPcId})", name, externalPcId);
                return new CommandResultEnvelope(
                    Constants.ControllerChannel.MessageType.CommandResult, commandId, "error",
                    ErrorCode: ErrorCode.CommandTimeout, Message: "command_timeout");
            }
        }
        finally
        {
            connection.PendingCommandId = null;
            connection.PendingResult = null;
            connection.CommandGate.Release();
        }
    }

    private static async Task SendJsonAsync(PcConnection connection, object envelope, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, ControllerJsonOptions.Default);
        await connection.SendLock.WaitAsync(ct);
        try
        {
            await connection.Socket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        finally
        {
            connection.SendLock.Release();
        }
    }

    private static CommandResultEnvelope OfflineResult(string commandName) => new(
        Constants.ControllerChannel.MessageType.CommandResult,
        CommandId: "cmd_" + Guid.NewGuid().ToString("N")[..12],
        Status: "error",
        ErrorCode: ErrorCode.AgentOffline,
        Message: $"agent not connected for {commandName}");

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var context = await _http.GetContextAsync();
                var connectionTask = Task.Run(() => HandleConnectionAsync(context, ct), ct);
                _connectionTasks[connectionTask] = 0;
                _ = connectionTask.ContinueWith(
                    t => _connectionTasks.TryRemove(t, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
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

    private async Task HandleConnectionAsync(HttpListenerContext context, CancellationToken ct)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }

        var externalPcId = context.Request.QueryString["external_pc_id"];
        var authHeader = context.Request.Headers["Authorization"];
        var token = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authHeader["Bearer ".Length..]
            : null;

        if (string.IsNullOrWhiteSpace(externalPcId) || !_registry.ValidateToken(externalPcId, token))
        {
            _logger.LogWarning("Rad etilgan ulanish: external_pc_id={ExternalPcId}", externalPcId);
            context.Response.StatusCode = 401;
            context.Response.Close();
            return;
        }

        var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
        var connection = new PcConnection { Socket = wsContext.WebSocket, ExternalPcId = externalPcId };
        _connections[externalPcId] = connection;
        _stateStore.MarkConnected(externalPcId);
        _logger.LogInformation("Agent ulandi: {ExternalPcId}", externalPcId);

        try
        {
            await ReceiveLoopAsync(connection, ct);
        }
        finally
        {
            _connections.TryRemove(externalPcId, out _);
            _stateStore.MarkDisconnected(externalPcId);
            connection.PendingResult?.TrySetException(new IOException("connection closed"));
            _logger.LogInformation("Agent uzildi: {ExternalPcId}", externalPcId);
        }
    }

    private async Task ReceiveLoopAsync(PcConnection connection, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var socket = connection.Socket;
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
                    {
                        // Complete the close handshake — a peer awaiting a graceful CloseAsync()
                        // would otherwise hang forever waiting for this frame.
                        if (socket.State == WebSocketState.CloseReceived)
                            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                ms.Position = 0;
                await HandleIncomingMessageAsync(connection, ms, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket uzildi: {ExternalPcId}", connection.ExternalPcId);
        }
        catch (HttpListenerException ex)
        {
            // Benign shutdown race: HttpListener.Stop()/Close() invalidated the underlying native
            // handle while this ReceiveAsync had a pending IOCP completion against it — not a real
            // connection failure.
            _logger.LogDebug(ex, "HttpListener yopilishi bilan poyga (benign): {ExternalPcId}", connection.ExternalPcId);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Socket allaqachon dispose qilingan (benign): {ExternalPcId}", connection.ExternalPcId);
        }
    }

    private async Task HandleIncomingMessageAsync(PcConnection connection, Stream body, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return;

            switch (typeProp.GetString())
            {
                case Constants.ControllerChannel.MessageType.CommandResult:
                    var resultEnvelope = root.Deserialize<CommandResultEnvelope>(ControllerJsonOptions.Default);
                    if (resultEnvelope is not null && resultEnvelope.CommandId == connection.PendingCommandId)
                        connection.PendingResult?.TrySetResult(resultEnvelope);
                    break;

                case Constants.ControllerChannel.MessageType.Event:
                    var eventEnvelope = root.Deserialize<EventEnvelope>(ControllerJsonOptions.Default);
                    if (eventEnvelope is not null)
                    {
                        if (_eventIdempotency.TryMarkProcessed(eventEnvelope.EventId))
                            _stateStore.ApplyEvent(connection.ExternalPcId, eventEnvelope);

                        await SendJsonAsync(connection,
                            new EventAckEnvelope(Constants.ControllerChannel.MessageType.EventAck, eventEnvelope.EventId), ct);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kiruvchi xabarni qayta ishlashda xato: {ExternalPcId}", connection.ExternalPcId);
        }
    }

    /// <summary>No push event carries a mid-session remaining_seconds (only time_low, at the
    /// 10/5/1-minute thresholds) — this periodic get_status poll keeps the live PC grid accurate
    /// between thresholds without touching the wire contract itself.</summary>
    private async Task StatusPollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(StatusPollIntervalSeconds), ct);

                foreach (var externalPcId in _connections.Keys.ToList())
                {
                    if (ct.IsCancellationRequested)
                        break;

                    try
                    {
                        var result = await SendCommandAsync(
                            externalPcId,
                            Constants.ControllerChannel.CommandName.GetStatus,
                            new GetStatusPayload(externalPcId),
                            TimeSpan.FromSeconds(StatusPollTimeoutSeconds),
                            ct);

                        if (result.Status == "ok" && result.Payload is JsonElement element)
                        {
                            var status = element.Deserialize<GetStatusResult>(ControllerJsonOptions.Default);
                            if (status is not null)
                                _stateStore.ApplyGetStatusResult(externalPcId, status);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "get_status so'rovida xato: {ExternalPcId}", externalPcId);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }
}
