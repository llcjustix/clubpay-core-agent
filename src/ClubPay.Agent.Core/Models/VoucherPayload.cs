namespace ClubPay.Agent.Core.Models;

/// <summary>ТЗ §6/10/13 offline voucher payload — the part of the token that gets signed. VoucherId
/// doubles as the replay nonce (contract §7 grant_id idempotency store keys on it, see VoucherService).
/// Exactly one of ExternalPcId/ClubId must be set — a voucher not bound to anything is rejected.</summary>
public sealed record VoucherPayload(
    string VoucherId,
    string? ExternalPcId,
    string? ClubId,
    int Seconds,
    DateTime ExpiresAtUtc);
