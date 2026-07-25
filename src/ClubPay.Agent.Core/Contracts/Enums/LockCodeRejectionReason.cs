namespace ClubPay.Agent.Core.Contracts.Enums;

/// <summary>ТЗ §7: why a code typed into the LockScreen's single input field was not accepted —
/// the unified, user-presentable rejection reason for both voucher and manager master-code paths.
/// Deliberately coarser than <see cref="VoucherRejectionReason"/>: the UI never needs to distinguish
/// a bad signature from malformed base64 (both are just "wrong code" to a person at the keyboard).</summary>
public enum LockCodeRejectionReason
{
    /// <summary>Not a recognizable token at all, bad signature, or malformed payload.</summary>
    InvalidCode,

    /// <summary>now &gt;= expires_at.</summary>
    Expired,

    /// <summary>The code is bound to a different PC or club.</summary>
    WrongPc,

    /// <summary>Replay: this one-time code was already applied on this PC.</summary>
    AlreadyUsed,

    /// <summary>The PC is not in a state the code can act on (active/frozen session, repair, ...).</summary>
    PcBusy,

    /// <summary>Verification key not configured/importable — the agent cannot accept codes right now.</summary>
    ServiceUnavailable,
}
