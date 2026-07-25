using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>ТЗ §6/10/13: locally verifies an Ed25519-signed offline voucher token (public key only —
/// the private key that signs vouchers lives on the Billing side and never reaches the agent) and, if
/// valid, starts a local session for it. Works with no network at all.</summary>
public interface IVoucherService
{
    Task<VoucherRedemptionResult> RedeemAsync(string token, CancellationToken ct = default);
}
