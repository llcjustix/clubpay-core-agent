namespace ClubPay.Agent.Core.Contracts.Enums;

/// <summary>ТЗ §13: why an offline voucher token was not accepted. Never surfaced on the wire — this is
/// a purely local (LockScreen) rejection reason, distinct from the Controller's <see cref="ErrorCode"/>.</summary>
public enum VoucherRejectionReason
{
    /// <summary>Token isn't well-formed compact serialization (wrong segment count, bad base64, or the
    /// decoded payload isn't valid JSON) — or the payload has neither external_pc_id nor club_id.</summary>
    MalformedToken,

    /// <summary>Ed25519 signature doesn't verify against the configured public key.</summary>
    InvalidSignature,

    /// <summary>now &gt;= expires_at.</summary>
    Expired,

    /// <summary>external_pc_id/club_id in the payload doesn't match this agent.</summary>
    WrongPc,

    /// <summary>voucher_id (used as grant_id) was already applied — contract §7 idempotency store.</summary>
    AlreadyUsed,

    /// <summary>ISessionCoordinator rejected the resulting start_session (e.g. pc_in_repair, pc_busy).</summary>
    SessionRejected,

    /// <summary>Voucher public key isn't configured or failed to import — the service can't verify anything.</summary>
    ServiceUnavailable,
}
