using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

public interface IControllerListener
{
    event Action<SessionStartCommand>? SessionStartReceived;
    event Action? SessionEndReceived;
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
}
