using ClubPay.Agent.Core.Contracts.Enums;

namespace ClubPay.Agent.Core.Contracts.Payloads;

/// <summary>Contract §4.1 start_session payload.</summary>
/// <remarks>PaymentOrderId is preserved/round-tripped only — the agent never inspects it (payment is out of scope here).</remarks>
public sealed record StartSessionPayload(
    string ExternalPcId,
    string GrantId,
    string? PaymentOrderId,
    int GrantedSeconds,
    DateTime EndsAt,
    string? Zone,
    DateTime? StartAt,
    // A short-lived, backend-issued URL for the currently active session. The Agent only displays
    // it and must never attempt to manufacture a replacement URL locally.
    string? ExtendUrl = null);

/// <summary>Contract §4.2 extend_session payload.</summary>
public sealed record ExtendSessionPayload(
    string CoreSessionId,
    string GrantId,
    string? PaymentOrderId,
    int AddedSeconds);

/// <summary>Contract §4.3 end_session payload.</summary>
public sealed record EndSessionPayload(string CoreSessionId, EndReason Reason);
