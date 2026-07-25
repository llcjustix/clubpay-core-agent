using ClubPay.Agent.Core.Contracts.Enums;

namespace ClubPay.Agent.Core.Models;

/// <summary>Result of <see cref="Services.IVoucherService.RedeemAsync"/> — never thrown, always returned,
/// so a ViewModel can render it directly without a try/catch (CLAUDE.md: no technical errors to the user).</summary>
public sealed record VoucherRedemptionResult(
    bool Accepted,
    string? CoreSessionId,
    int RemainingSeconds,
    VoucherRejectionReason? RejectionReason,
    string? Message)
{
    public static VoucherRedemptionResult Ok(string coreSessionId, int remainingSeconds) =>
        new(true, coreSessionId, remainingSeconds, null, null);

    public static VoucherRedemptionResult Rejected(VoucherRejectionReason reason, string message) =>
        new(false, null, 0, reason, message);
}
