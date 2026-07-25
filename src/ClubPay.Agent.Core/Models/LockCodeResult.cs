using ClubPay.Agent.Core.Contracts.Enums;

namespace ClubPay.Agent.Core.Models;

/// <summary>What kind of code the LockScreen input turned out to contain.</summary>
public enum LockCodeKind
{
    Unknown,
    Voucher,
    ManagerCode,
}

/// <summary>Result of <see cref="Services.ILockCodeService.SubmitAsync"/> — never thrown, always
/// returned, so the LockScreen ViewModel only maps <see cref="RejectionReason"/> to a friendly message.</summary>
public sealed record LockCodeResult(
    bool Accepted,
    LockCodeKind Kind,
    LockCodeRejectionReason? RejectionReason)
{
    public static LockCodeResult Ok(LockCodeKind kind) => new(true, kind, null);

    public static LockCodeResult Rejected(LockCodeKind kind, LockCodeRejectionReason reason) =>
        new(false, kind, reason);
}
