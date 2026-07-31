using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Exceptions;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>
/// Default ICommandValidator implementation. Depends only on other Core interfaces (IAgentService,
/// ISessionCoordinator, ISystemClock), so it stays on the Core side of the one-directional
/// Core→Client dependency rule despite reading live agent/session state.
/// </summary>
public sealed class CommandValidationService(IAgentService agent, ISessionCoordinator coordinator, ISystemClock clock)
    : ICommandValidator
{
    public void ValidateCommandId(string commandId) =>
        RequireNonEmpty(commandId, "command_id");

    public void ValidateStartSession(StartSessionPayload payload)
    {
        RequireOwnPc(payload.ExternalPcId);
        RequireNonEmpty(payload.GrantId, "grant_id");
        RequirePositiveWithinLimit(payload.GrantedSeconds, Constants.SessionCommand.MaxGrantedSeconds, "granted_seconds");
        RequireValidZone(payload.Zone);
        RequireLogicalTimes(payload.StartAt, payload.EndsAt);
    }

    public void ValidateExtendSession(ExtendSessionPayload payload)
    {
        RequireNonEmpty(payload.GrantId, "grant_id");
        RequirePositiveWithinLimit(payload.AddedSeconds, Constants.SessionCommand.MaxAddedSeconds, "added_seconds");
        RequireOwnCoreSessionId(payload.CoreSessionId);
    }

    public void ValidateEndSession(EndSessionPayload payload) =>
        RequireOwnCoreSessionId(payload.CoreSessionId);

    public void ValidateLock(LockPayload payload) => RequireOwnPc(payload.ExternalPcId);

    public void ValidateUnlock(UnlockPayload payload) => RequireOwnPc(payload.ExternalPcId);

    public void ValidateSleep(SleepPayload payload) => RequireOwnPc(payload.ExternalPcId);

    public void ValidateSetRepair(SetRepairPayload payload) => RequireOwnPc(payload.ExternalPcId);

    private void RequireOwnPc(string externalPcId)
    {
        RequireNonEmpty(externalPcId, "external_pc_id");
        if (!string.Equals(externalPcId, agent.ExternalPcId, StringComparison.Ordinal))
            throw new SessionCommandException(ErrorCode.InvalidState, "external_pc_id does not match this agent");
    }

    private void RequireOwnCoreSessionId(string coreSessionId)
    {
        RequireNonEmpty(coreSessionId, "core_session_id");

        Session? current = coordinator.CurrentSession;
        if (current is null)
            return; // No active session — the coordinator's own "no active session" guard fires instead.

        var expected = current.CoreSessionId?.ToString("N");
        if (!string.Equals(coreSessionId, expected, StringComparison.OrdinalIgnoreCase))
            throw new SessionCommandException(ErrorCode.InvalidState, "core_session_id does not match the active session");
    }

    private static void RequireNonEmpty(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new SessionCommandException(ErrorCode.InvalidState, $"{fieldName} is required");
    }

    private static void RequirePositiveWithinLimit(int value, int maxValue, string fieldName)
    {
        if (value <= 0)
            throw new SessionCommandException(ErrorCode.InvalidState, $"{fieldName} must be positive");
        if (value > maxValue)
            throw new SessionCommandException(ErrorCode.InvalidState, $"{fieldName} exceeds the allowed maximum");
    }

    private static void RequireValidZone(string? zone)
    {
        if (string.IsNullOrWhiteSpace(zone))
            return; // Unspecified — SessionCoordinatorService.ParseZone's Standard fallback stays for this case only.
        if (!Enum.TryParse<ZoneType>(zone, ignoreCase: true, out _))
            throw new SessionCommandException(ErrorCode.InvalidState, "zone is not a recognized value");
    }

    private void RequireLogicalTimes(DateTime? startAt, DateTime endsAt)
    {
        var effectiveStart = startAt ?? clock.UtcNow;
        if (endsAt <= effectiveStart)
            throw new SessionCommandException(ErrorCode.InvalidState, "ends_at must be after start_at");
    }
}
