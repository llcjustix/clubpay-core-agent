using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly IControllerOutbox _outbox;
    private readonly IReadOnlyList<string> _webSocketUrls;
    private readonly string _agentToken;
    private readonly string _externalPcId;
    private readonly ILogger<ControllerChannelService> _logger;
    private readonly TimeSpan _endpointConnectTimeout;
    private readonly TimeSpan _primaryRecoveryProbeInterval;

    private readonly SemaphoreSlim _sendSignal = new(0);
    private CancellationTokenSource? _lifetimeCts;
    private Task? _runLoopTask;
    private string? _activeConnectionId;

    public ChannelConnectionState ConnectionState { get; private set; } = ChannelConnectionState.Disconnected;
    public string? ActiveEndpoint { get; private set; }
    public event Action<ChannelConnectionState>? ConnectionStateChanged;

    // ICommandDispatcher is resolved lazily via IServiceProvider rather than taken as a direct
    // constructor dependency: ICommandDispatcher -> ISessionCoordinator -> IControllerChannel would
    // otherwise be a genuine DI construction cycle (verified by actually running the composed app —
    // the container refuses to build it). Resolving it on first use, once the whole container already
    // exists, breaks the cycle without reintroducing a "ViewModel subscribes to transport" anti-pattern.
    public ControllerChannelService(
        IServiceProvider serviceProvider,
        IControllerOutbox outbox,
        IConfiguration config,
        ILogger<ControllerChannelService> logger)
    {
        _serviceProvider = serviceProvider;
        _outbox = outbox;
        _logger = logger;
        _webSocketUrls = ReadUrls(config, "Controller:WebSocketUrl", "Controller:FallbackWebSocketUrls");
        _agentToken = config["Controller:AgentToken"] ?? string.Empty;
        _externalPcId = config["Controller:ExternalPcId"] ?? string.Empty;
        _endpointConnectTimeout = TimeSpan.FromSeconds(Clamp(config.GetValue<int?>("Controller:FailoverTimeoutSeconds") ?? 8, 2, 60));
        _primaryRecoveryProbeInterval = TimeSpan.FromSeconds(Clamp(config.GetValue<int?>("Controller:PrimaryRecoveryProbeSeconds") ?? 5, 1, 60));
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_runLoopTask is not null)
            return Task.CompletedTask;

        if (_webSocketUrls.Count == 0)
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
        // The Controller that receives an event must be able to fence events
        // from a previous socket after a primary → Manager failover.  This ID
        // changes on every successful WebSocket connection and is persisted in
        // the outbox payload, so replayed events are still attributable to the
        // current Agent connection.
        var evt = new EventEnvelope(
            Constants.ControllerChannel.MessageType.Event, eventName, "ev_" + Guid.NewGuid().ToString("N"), DateTime.UtcNow,
            WithConnectionIdentity(payload));

        try
        {
            await _outbox.EnqueueAsync(evt, ct);
            _sendSignal.Release();
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
        _sendSignal.Dispose();
    }

    private async Task RunConnectionLoopAsync(CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            SetState(attempt == 0 ? ChannelConnectionState.Connecting : ChannelConnectionState.Reconnecting);
            var connected = false;
            for (var endpointIndex = 0; endpointIndex < _webSocketUrls.Count; endpointIndex++)
            {
                var endpoint = _webSocketUrls[endpointIndex];
                ClientWebSocket? socket = null;
                try
                {
                    socket = new ClientWebSocket();
                    socket.Options.SetRequestHeader("Authorization", $"Bearer {_agentToken}");
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(Constants.ControllerChannel.HeartbeatIntervalSeconds);
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    connectCts.CancelAfter(_endpointConnectTimeout);
                    await socket.ConnectAsync(BuildUri(endpoint), connectCts.Token);

                    connected = true;
                    ActiveEndpoint = endpoint;
                    _activeConnectionId = Guid.NewGuid().ToString("N");
                    SetState(ChannelConnectionState.Connected);
                    attempt = 0;
                    AgentRuntimeHealth.MarkControllerConnected();
                    _logger.LogInformation("Controller channel connected to {Endpoint} ({Role})", endpoint,
                        endpointIndex == 0 ? "primary" : "fallback");
                    await PublishEventAsync(Constants.ControllerChannel.EventName.AgentOnline, new AgentOnlineEvent(_externalPcId), ct);

                    var receiveTask = ReceiveLoopAsync(socket, ct);
                    var sendTask = SendLoopAsync(socket, ct);
                    if (endpointIndex == 0)
                    {
                        await Task.WhenAny(receiveTask, sendTask);
                    }
                    else
                    {
                        var primaryRecovered = WaitForPrimaryRecoveryAsync(ct);
                        var completed = await Task.WhenAny(receiveTask, sendTask, primaryRecovered);
                        if (completed == primaryRecovered && !ct.IsCancellationRequested)
                        {
                            _logger.LogInformation("Primary Controller is healthy again; returning from fallback {Endpoint}", endpoint);
                            try
                            {
                                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "primary_controller_recovered", ct);
                            }
                            catch (WebSocketException)
                            {
                                // Disposing below is sufficient if the fallback is already gone.
                            }
                        }
                    }
                    break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Controller channel endpoint {Endpoint} is unavailable", endpoint);
                }
                finally
                {
                    if (string.Equals(ActiveEndpoint, endpoint, StringComparison.Ordinal))
                    {
                        ActiveEndpoint = null;
                        _activeConnectionId = null;
                    }
                    socket?.Dispose();
                }
            }

            if (ct.IsCancellationRequested)
                break;

            if (!connected)
                _logger.LogWarning("All configured Controller endpoints are unavailable");
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
            var dispatcher = _serviceProvider.GetRequiredService<ICommandDispatcher>();
            var result = await dispatcher.DispatchAsync(command, ct);

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
                        await _sendSignal.WaitAsync(TimeSpan.FromSeconds(5), ct);
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

    private Uri BuildUri(string endpoint)
    {
        var builder = new UriBuilder(endpoint);
        var existingQuery = builder.Query.TrimStart('?');
        var pcIdParam = $"external_pc_id={Uri.EscapeDataString(_externalPcId)}";
        builder.Query = string.IsNullOrEmpty(existingQuery) ? pcIdParam : $"{existingQuery}&{pcIdParam}";
        return builder.Uri;
    }

    private static IReadOnlyList<string> ReadUrls(IConfiguration config, string primaryKey, string fallbacksKey)
    {
        var urls = new List<string>();
        AddUrl(urls, config[primaryKey]);
        foreach (var child in config.GetSection(fallbacksKey).GetChildren())
            AddUrl(urls, child.Value);
        foreach (var value in (config[fallbacksKey] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            AddUrl(urls, value);
        return urls;
    }

    private static void AddUrl(ICollection<string> urls, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !urls.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
            urls.Add(value.Trim());
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

    private async Task WaitForPrimaryRecoveryAsync(CancellationToken ct)
    {
        if (_webSocketUrls.Count == 0)
            return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_primaryRecoveryProbeInterval, ct);
                using var client = new HttpClient { Timeout = _endpointConnectTimeout };
                using var response = await client.GetAsync(BuildHealthUri(_webSocketUrls[0]), ct);
                if (response.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices)
                    return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // The primary is still unavailable; remain on the Manager.
            }
        }
    }

    private static Uri BuildHealthUri(string endpoint)
    {
        var websocket = new Uri(endpoint);
        var builder = new UriBuilder(websocket)
        {
            Scheme = websocket.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase) ? "https" : "http",
            Path = "/api/health",
            Query = string.Empty,
        };
        return builder.Uri;
    }

    private Dictionary<string, object?> WithConnectionIdentity(object payload)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var encoded = JsonSerializer.SerializeToElement(payload, ControllerJsonOptions.Default);
        if (encoded.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in encoded.EnumerateObject())
                result[property.Name] = property.Value.Clone();
        }
        else
        {
            result["data"] = encoded.Clone();
        }
        result["agent_connection_id"] = _activeConnectionId ?? string.Empty;
        return result;
    }

    private static int Clamp(int value, int minimum, int maximum) => Math.Clamp(value, minimum, maximum);
}
