namespace ClubPay.Agent.Core.Models;

public record VoucherToken(
    Guid Id,
    string PcId,
    int RemainingSeconds,
    DateTime ExpiresAtUtc,
    byte[] Signature
)
{
    public bool IsExpired(DateTime nowUtc) => nowUtc > ExpiresAtUtc;

    public string Code => Id.ToString("N")[..8].ToUpper();
}
