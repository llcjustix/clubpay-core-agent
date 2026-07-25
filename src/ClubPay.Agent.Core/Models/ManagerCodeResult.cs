using ClubPay.Agent.Core.Contracts.Enums;

namespace ClubPay.Agent.Core.Models;

/// <summary>Result of <see cref="Services.IManagerCodeService.RedeemAsync"/> — never thrown, always
/// returned, so a ViewModel can render it directly without a try/catch (CLAUDE.md: no technical errors
/// to the user).</summary>
public sealed record ManagerCodeResult(
    bool Accepted,
    LockCodeRejectionReason? RejectionReason,
    string? Message)
{
    public static ManagerCodeResult Ok() => new(true, null, null);

    public static ManagerCodeResult Rejected(LockCodeRejectionReason reason, string message) =>
        new(false, reason, message);
}
