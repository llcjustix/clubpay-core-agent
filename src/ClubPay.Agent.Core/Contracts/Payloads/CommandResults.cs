using ClubPay.Agent.Core.Contracts.Enums;

namespace ClubPay.Agent.Core.Contracts.Payloads;

/// <summary>Contract §4.1/§7 result — RemainingSeconds is populated on both a fresh start and an
/// idempotent duplicate replay (grant_id already applied).</summary>
public sealed record StartSessionResult(
    string CoreSessionId,
    int RemainingSeconds,
    string ExternalPcId,
    string GrantId,
    DateTime StartedAt,
    DateTime? EndsAt);

/// <summary>Contract §4.2/§7 result.</summary>
public sealed record ExtendSessionResult(
    int RemainingSeconds,
    string CoreSessionId,
    string GrantId,
    DateTime NewEndsAt);

/// <summary>Contract §4.3 result.</summary>
public sealed record EndSessionResult(int ConsumedSeconds, int RemainingSeconds);

/// <summary>Contract §4.7 result.</summary>
public sealed record GetStatusResult(
    PcState PcState,
    SessionState? SessionState,
    string? CoreSessionId,
    int? RemainingSeconds);

/// <summary>Ack-only result for lock/unlock/wake/sleep/set_repair.</summary>
public sealed record EmptyResult;
