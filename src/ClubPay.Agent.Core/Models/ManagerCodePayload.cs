namespace ClubPay.Agent.Core.Models;

/// <summary>ТЗ §7/§10 manager master-code payload — the part of the token that gets signed. CodeId
/// doubles as the replay nonce (persisted via IGrantIdempotencyStore under "mgr:" + CodeId; that store
/// prunes records after ~30 days, so the issuer must keep ExpiresAtUtc within 30 days of issuance or a
/// pruned code would become replayable). Exactly one of ExternalPcId/ClubId must be set. The wire shape
/// is snake_case JSON: "code_id" (vs a voucher's "voucher_id") is what lets LockCodeService auto-detect
/// which kind of code the person typed.</summary>
public sealed record ManagerCodePayload(
    string CodeId,
    string ManagerId,
    string? ExternalPcId,
    string? ClubId,
    int Seconds,
    DateTime ExpiresAtUtc);
