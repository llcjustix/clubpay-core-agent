using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts;
using ClubPay.Agent.Core.Contracts.Events;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Client.Services;

/// <summary>
/// Pure transport: opens an OUTBOUND WebSocket to the Controller (contract §1 — the agent has no
/// static IP, so it must always be the one initiating the connection) and reconnects with backoff +
/// jitter on failure. Incoming commands are handed to ICommandDispatcher; outgoing events are queued
/// in IControllerOutbox (the single source of truth — this class never holds a second in-memory copy,
/// so a mid-send disconnect can never cause a duplicate resend).
/// </summary>
public sealed class ControllerChannelService : IControllerChannel
{
    private readonly IControllerOutbox _outbox;
    private readonly string _webSocketUrl;
    private readonly string _agentToken;
    private readonly string _externalPcId;
    private readonly TimeSpan _eventAckTimeout;
    private readonly int _maxEventSendRetries;
    private readonly ILogger<ControllerChannelService> _logger;

    // Correlates an in-flight durable-event send (SendLoopAsync's task) with the event_ack that
    // completes it (ReceiveLoopAsync's task) — guarded by _ackGate since the two run concurrently.
    private readonly object _ackGate = new();
    private string? _pendingAckEventId;
    private TaskCompletionSource<bool>? _pendingAckTcs;

    // A WebSocket only tolerates one outstanding SendAsync at a time; SendLoopAsync (events) and
    // ReceiveLoopAsync (command_result replies, now also nothing extra) both write to the same socket
    // concurrently, so every raw send goes through this lock.
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private CancellationTokenSource? _lifetimeCts;
    private Task? _runLoopTask;

    public ChannelConnectionState ConnectionState { get; private set; } = ChannelConnectionState.Disconnected;
    public event Action<ChannelConnectionState>? ConnectionStateChanged;

    // Assigned by the composition root (App.xaml.cs) after every singleton already exists — see the
    // interface doc comment for why this replaces a constructor dependency on ICommandDispatcher.
    public Func<CommandEnvelope, CancellationToken, Task<CommandResultEnvelope>>? IncomingCommandHandler { get; set; }

    public ControllerChannelService(
        IControllerOutbox outbox,
        IConfiguration config,
        ILogger<ControllerChannelService> logger)
    {
        _outbox = outbox;
        _logger = logger;
        _webSocketUrl = config["Controller:WebSocketUrl"] ?? string.Empty;
        _agentToken = config["Controller:AgentToken"] ?? string.Empty;
        _externalPcId = config["Controller:ExternalPcId"] ?? string.Empty;
        _eventAckTimeout = TimeSpan.FromSeconds(
            config.GetValue("Controller:EventAckTimeoutSeconds", Constants.ControllerChannel.SendTimeoutSeconds));
        _maxEventSendRetries =
            config.GetValue("Controller:MaxEventSendRetries", Constants.ControllerChannel.MaxSendRetries);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_runLoopTask is not null)
            return Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(_webSocketUrl))
        {
            _logger.LogWarning("Controller:WebSocketUrl sozlanmagan — kanal ishga tushmaydi");
            return Task.CompletedTask;
        }

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runLoopTask = Task.Run(() => RunConnectionLoopAsync(_lifetimeCts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_lifetimeCts is null)
            return;

        _lifetimeCts.Cancel();
        try
        {
            if (_runLoopTask is not null)
                await _runLoopTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            _logger.LogWarning(ex, "Controller kanalini yopishda kutish tugadi");
        }
    }

    public async Task PublishEventAsync(string eventName, object payload, CancellationToken ct = default)
    {
        try
        {
            await _outbox.PublishEventAsync(eventName, payload, ct);
            _logger.LogInformation(
                "Event navbatga qo'yildi: {Event} {Payload}",
                eventName,
                JsonSerializer.Serialize(payload, ControllerJsonOptions.Default));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Event} hodisasini navbatga qo'yib bo'lmadi", eventName);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifetimeCts?.Dispose();
        _sendLock.Dispose();
    }

    private async Task RunConnectionLoopAsync(CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            ClientWebSocket? socket = null;
            try
            {
                SetState(attempt == 0 ? ChannelConnectionState.Connecting : ChannelConnectionState.Reconnecting);

                socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", $"Bearer {_agentToken}");
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(Constants.ControllerChannel.HeartbeatIntervalSeconds);
                await socket.ConnectAsync(BuildUri(), ct);

                SetState(ChannelConnectionState.Connected);
                attempt = 0;
                _logger.LogInformation("Controller kanaliga ulandi (WebSocket handshake, 101 Switching Protocols)");

                await PublishEventAsync(Constants.ControllerChannel.EventName.AgentOnline, new AgentOnlineEvent(_externalPcId), ct);
                _logger.LogInformation("agent_online yuborildi: external_pc_id={ExternalPcId}", _externalPcId);

                var receiveTask = ReceiveLoopAsync(socket, ct);
                var sendTask = SendLoopAsync(socket, ct);
                await Task.WhenAny(receiveTask, sendTask);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Controller kanaliga ulanishda xato");
            }
            finally
            {
                socket?.Dispose();
            }

            if (ct.IsCancellationRequested)
                break;

            attempt++;
            SetState(ChannelConnectionState.Reconnecting);
            try
            {
                await Task.Delay(ComputeBackoffDelay(attempt), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        SetState(ChannelConnectionState.Disconnected);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
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
                await HandleIncomingMessageAsync(socket, ms, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qabul qilish tsiklida xato — qayta ulanish kutilmoqda");
        }
    }

    private async Task HandleIncomingMessageAsync(ClientWebSocket socket, Stream body, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return;

            switch (typeProp.GetString())
            {
                case Constants.ControllerChannel.MessageType.Command:
                    await HandleCommandMessageAsync(socket, root, ct);
                    break;

                case Constants.ControllerChannel.MessageType.EventAck:
                    HandleEventAck(root);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kiruvchi xabarni qayta ishlashda xato");
        }
    }

    private async Task HandleCommandMessageAsync(ClientWebSocket socket, JsonElement root, CancellationToken ct)
    {
        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
        var commandId = root.TryGetProperty("command_id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
        var ts = root.TryGetProperty("ts", out var tsProp) && tsProp.TryGetDateTime(out var parsedTs)
            ? parsedTs
            : DateTime.UtcNow;
        object? payload = root.TryGetProperty("payload", out var payloadProp) ? payloadProp.Clone() : null;

        var command = new CommandEnvelope(Constants.ControllerChannel.MessageType.Command, name, commandId, ts, payload);
        if (IncomingCommandHandler is not { } handler)
        {
            _logger.LogWarning("IncomingCommandHandler sozlanmagan — buyruq e'tiborsiz qoldirildi: {Name}", name);
            return;
        }

        var result = await handler(command, ct);

        _logger.LogInformation(
            "Command_result yuborildi: {Name} ({CommandId}) {Payload}",
            name,
            commandId,
            JsonSerializer.Serialize(result, ControllerJsonOptions.Default));
        await SendJsonAsync(socket, result, ct);
    }

    private void HandleEventAck(JsonElement root)
    {
        var eventId = root.TryGetProperty("event_id", out var idProp) ? idProp.GetString() : null;
        if (eventId is null)
            return;

        lock (_ackGate)
        {
            if (_pendingAckEventId == eventId)
                _pendingAckTcs?.TrySetResult(true);
        }
    }

    private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var pending = await _outbox.GetPendingAsync(ct);
                if (pending.Count == 0)
                {
                    try
                    {
                        await _outbox.WaitForPendingAsync(TimeSpan.FromSeconds(5), ct);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    continue;
                }

                foreach (var evt in pending)
                {
                    if (socket.State != WebSocketState.Open || ct.IsCancellationRequested)
                        return;

                    if (Constants.ControllerChannel.IsTelemetryEvent(evt.Name))
                    {
                        await SendJsonAsync(socket, evt, ct);
                        await _outbox.MarkSentAsync(evt.EventId, ct);
                        continue;
                    }

                    if (!await SendDurableEventWithAckAsync(socket, evt, ct))
                        return; // retries exhausted — tear the connection down; the next connection's
                                // fresh SendLoopAsync call resends this same still-pending row at attempt 1
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Yuborish tsiklida xato — qayta ulanish kutilmoqda");
        }
    }

    /// <summary>Stop-and-wait delivery for a durable (money/session) event: sends, waits up to
    /// _eventAckTimeout for a correlated event_ack, and retries the same event on timeout up to
    /// _maxEventSendRetries times. Never advances to the next pending event until this one is acked or
    /// retries are exhausted — that single-event-in-flight rule is what keeps outbox ordering intact.
    /// Returns false once retries are exhausted (caller must tear the connection down and let reconnect
    /// pick this event up fresh).</summary>
    private async Task<bool> SendDurableEventWithAckAsync(ClientWebSocket socket, EventEnvelope evt, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= _maxEventSendRetries; attempt++)
        {
            var ackTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_ackGate)
            {
                _pendingAckEventId = evt.EventId;
                _pendingAckTcs = ackTcs;
            }

            try
            {
                await SendJsonAsync(socket, evt, ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_eventAckTimeout);
                await ackTcs.Task.WaitAsync(timeoutCts.Token);

                await _outbox.MarkSentAsync(evt.EventId, ct);
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "event_ack kutish vaqti tugadi: {Name} ({EventId}), urinish {Attempt}/{Max}",
                    evt.Name, evt.EventId, attempt, _maxEventSendRetries);
            }
            finally
            {
                // Identity check, not just event_id equality: if this connection attempt was abandoned
                // (e.g. RunConnectionLoopAsync already tore it down after a hard disconnect) while this
                // call was still waiting, a *new* connection's SendDurableEventWithAckAsync call for the
                // same still-pending event_id may already have registered its own TCS by the time this
                // orphaned attempt reaches here. Clearing unconditionally on event_id match would wipe
                // out that newer wait out from under it, causing its real ack to be silently dropped.
                lock (_ackGate)
                {
                    if (ReferenceEquals(_pendingAckTcs, ackTcs))
                    {
                        _pendingAckEventId = null;
                        _pendingAckTcs = null;
                    }
                }
            }
        }

        _logger.LogWarning(
            "event_ack olinmadi, urinishlar tugadi — ulanish qayta o'rnatiladi: {Name} ({EventId})",
            evt.Name, evt.EventId);
        return false;
    }

    private async Task SendJsonAsync(ClientWebSocket socket, object envelope, CancellationToken ct)
    {
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

    private Uri BuildUri()
    {
        var builder = new UriBuilder(_webSocketUrl);
        var existingQuery = builder.Query.TrimStart('?');
        var pcIdParam = $"external_pc_id={Uri.EscapeDataString(_externalPcId)}";
        var tokenParam = $"agent_token={Uri.EscapeDataString(_agentToken)}";
        var newParams = $"{pcIdParam}&{tokenParam}";
        builder.Query = string.IsNullOrEmpty(existingQuery) ? newParams : $"{existingQuery}&{newParams}";
        return builder.Uri;
    }

    private static TimeSpan ComputeBackoffDelay(int attempt)
    {
        double baseDelay = Constants.ControllerChannel.ReconnectBaseDelaySeconds;
        double maxDelay = Constants.ControllerChannel.ReconnectMaxDelaySeconds;
        double exp = Math.Min(maxDelay, baseDelay * Math.Pow(2, attempt - 1));
        int jitterMs = Random.Shared.Next(0, Constants.ControllerChannel.ReconnectMaxJitterMs);
        return TimeSpan.FromSeconds(exp) + TimeSpan.FromMilliseconds(jitterMs);
    }

    private void SetState(ChannelConnectionState state)
    {
        ConnectionState = state;
        ConnectionStateChanged?.Invoke(state);
    }
}
