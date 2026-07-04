using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

public interface ICoreApiListener
{
    event Action<CoreSessionStartCommand>? SessionStartReceived;
    event Action<CoreSessionExtendCommand>? SessionExtendReceived;
    event Action<CoreSessionEndCommand>? SessionEndReceived;
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
}
