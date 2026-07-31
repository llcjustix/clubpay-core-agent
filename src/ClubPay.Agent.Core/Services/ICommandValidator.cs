using ClubPay.Agent.Core.Contracts.Payloads;

namespace ClubPay.Agent.Core.Services;

/// <summary>
/// Centralized, pure (no I/O, no mutation) validation of incoming controller command payloads, run by
/// CommandDispatcherService after deserialization and before any ISessionCoordinator call. Every
/// rejection throws SessionCommandException(ErrorCode.InvalidState, ...), which the dispatcher's
/// existing catch block turns into a command_failed event and an error command_result — guaranteeing
/// zero state mutation on any validation failure.
/// </summary>
public interface ICommandValidator
{
    void ValidateCommandId(string commandId);
    void ValidateStartSession(StartSessionPayload payload);
    void ValidateExtendSession(ExtendSessionPayload payload);
    void ValidateEndSession(EndSessionPayload payload);
    void ValidateLock(LockPayload payload);
    void ValidateUnlock(UnlockPayload payload);
    void ValidateSleep(SleepPayload payload);
    void ValidateSetRepair(SetRepairPayload payload);
}
