using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Core.Services;

/// <summary>ТЗ §7/§10: locally verifies an Ed25519-signed one-time manager master code (public key only —
/// the private key that signs codes never reaches the agent) and, if valid, unlocks the PC: clears a
/// manager lock, or starts a session on an idle locked PC. Every acceptance is written to the controller
/// outbox as a manager_unlock audit event. Works with no network at all.</summary>
public interface IManagerCodeService
{
    Task<ManagerCodeResult> RedeemAsync(string token, CancellationToken ct = default);
}
