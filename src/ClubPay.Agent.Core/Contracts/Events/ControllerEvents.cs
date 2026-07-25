using ClubPay.Agent.Core.Contracts.Enums;

namespace ClubPay.Agent.Core.Contracts.Events;

/// <summary>Contract §5.0. Sent once right after a successful channel connect/handshake.</summary>
/// <remarks>There is no corresponding "SendAgentOfflineEvent" — an agent that can send is by definition
/// online. The Controller infers agent_offline itself from a missed heartbeat / dropped connection.</remarks>
public sealed record AgentOnlineEvent(string ExternalPcId);

/// <summary>Contract §5.0.</summary>
public sealed record CommandFailedEvent(string CommandId, string ExternalPcId, ErrorCode ErrorCode);

/// <summary>Contract §5.1.</summary>
public sealed record SessionStartedEvent(
    string CoreSessionId,
    string ExternalPcId,
    string GrantId,
    string? PaymentOrderId,
    DateTime StartedAt);

/// <summary>Contract §5.2b.</summary>
public sealed record SessionExtendedEvent(
    string CoreSessionId,
    string ExternalPcId,
    string GrantId,
    int AddedSeconds,
    DateTime EndsAt);

/// <summary>Session end — not in the contract's numbered examples directly but implied by §9 scenarios
/// (consumed_seconds/remaining_seconds reported alongside the end_session command_result).</summary>
public sealed record SessionEndedEvent(
    string CoreSessionId,
    string ExternalPcId,
    EndReason Reason,
    int ConsumedSeconds,
    int RemainingSeconds);

/// <summary>Contract §5.3 time_low.</summary>
public sealed record TimeLowEvent(string CoreSessionId, int RemainingSeconds, int Threshold);

/// <summary>ТЗ §7/§11 audit trail for a manager master-code acceptance on the LockScreen. Not yet part
/// of the numbered contract — a wire-contract extension the Controller may log or ignore until it
/// consumes it. Action is "unlock" (cleared a manager lock) or "start_session" (opened an idle PC).</summary>
public sealed record ManagerUnlockEvent(
    string ExternalPcId,
    string CodeId,
    string ManagerId,
    string Action,
    DateTime AtUtc);

/// <summary>Contract §5.4 pc_state_changed.</summary>
public sealed record PcStateChangedEvent(string ExternalPcId, PcState PcState, bool Online);

/// <summary>Contract §5.5 heartbeat.</summary>
public sealed record HeartbeatEvent(string ExternalPcId, PcState PcState, int ControllersSeen, bool ServerReachable);
