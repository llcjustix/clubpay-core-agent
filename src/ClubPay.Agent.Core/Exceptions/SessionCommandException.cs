using ClubPay.Agent.Core.Contracts.Enums;

namespace ClubPay.Agent.Core.Exceptions;

/// <summary>Represents an expected business-rule rejection from ISessionCoordinator (e.g. repair mode
/// active, grant already applied to a different session). CommandDispatcherService maps this to the
/// wire-level error_code; the coordinator itself stays unaware of command_id/wire format (SRP).</summary>
public sealed class SessionCommandException(ErrorCode errorCode, string message) : Exception(message)
{
    public ErrorCode ErrorCode { get; } = errorCode;
}
