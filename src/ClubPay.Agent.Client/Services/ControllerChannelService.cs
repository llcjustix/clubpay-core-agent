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
    private readonly ILogger<ControllerChannelService> _logger;

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

            if (!root.TryGetProperty("type", out var typeProp) ||
                typeProp.GetString() != Constants.ControllerChannel.MessageType.Command)
            {
                return;
            }

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

            var json = JsonSerializer.SerializeToUtf8Bytes(result, ControllerJsonOptions.Default);
            await socket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kiruvchi buyruqni qayta ishlashda xato");
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

                    var json = JsonSerializer.SerializeToUtf8Bytes(evt, ControllerJsonOptions.Default);
                    await socket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, endOfMessage: true, ct);
                    await _outbox.MarkSentAsync(evt.EventId, ct);
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
