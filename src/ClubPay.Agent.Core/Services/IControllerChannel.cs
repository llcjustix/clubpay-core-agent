namespace ClubPay.Agent.Core.Services;

public enum ChannelConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting
}

/// <summary>
/// Pure transport pipe: agent-initiated outbound WebSocket to the Controller (contract §1 — the agent
/// has no static IP, so it must always be the one opening the connection). Knows nothing about session
/// business logic; incoming commands are handed to <see cref="ICommandDispatcher"/>.
/// </summary>
public interface IControllerChannel : IAsyncDisposable
{
    ChannelConnectionState ConnectionState { get; }
    /// <summary>The WebSocket endpoint currently carrying this Agent's commands, if connected.</summary>
    string? ActiveEndpoint { get; }
    event Action<ChannelConnectionState>? ConnectionStateChanged;

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Queues an event for delivery to the Controller. If disconnected, it is persisted to the
    /// outbox and flushed on reconnect (contract §1/§8).</summary>
    Task PublishEventAsync(string eventName, object payload, CancellationToken ct = default);
}
