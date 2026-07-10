using ClubPay.Agent.Core.Contracts;

namespace ClubPay.Agent.Core.Services;

/// <summary>Routes an incoming CommandEnvelope to ISessionCoordinator and always returns a well-formed
/// CommandResultEnvelope — never leaves a command unanswered, even on an unknown name or an internal error.</summary>
public interface ICommandDispatcher
{
    Task<CommandResultEnvelope> DispatchAsync(CommandEnvelope command, CancellationToken ct = default);
}
