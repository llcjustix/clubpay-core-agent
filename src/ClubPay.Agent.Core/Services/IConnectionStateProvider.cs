namespace ClubPay.Agent.Core.Services;

/// <summary>
/// Read-only view of the Controller connection's health. Exists so consumers that only need to check
/// connectivity (e.g. <see cref="ISessionCoordinator"/> for heartbeat reporting) can depend on this
/// instead of the full <see cref="IControllerChannel"/> transport surface.
/// </summary>
public interface IConnectionStateProvider
{
    ChannelConnectionState ConnectionState { get; }
}
