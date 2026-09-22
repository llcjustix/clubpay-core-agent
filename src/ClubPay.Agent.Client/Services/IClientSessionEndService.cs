namespace ClubPay.Agent.Client.Services;

public sealed record ClientSessionEndResult(
    bool IsProfileSession,
    string? VoucherCode,
    int VoucherSeconds,
    int ProfileBalanceAddedSeconds,
    string DeliveryStatus,
    string? TelegramLink,
    string? TelegramBotUsername)
{
    // The completion dialog is meaningful only when a guest has an actual
    // voucher to keep. A zero-balance response must return straight to the
    // locked screen, even if a stale Controller cannot identify the profile.
    public bool HasVoucherToShow => !IsProfileSession &&
        (VoucherSeconds > 0 || !string.IsNullOrWhiteSpace(VoucherCode));
}

/// <summary>
/// Ends the current kiosk session through Core. Core remains the authority: it commands the Agent
/// to lock, saves unused time to a signed-in player's balance, and falls back to a voucher for guests.
/// </summary>
public interface IClientSessionEndService
{
    Task<ClientSessionEndResult> EndCurrentSessionAsync(CancellationToken ct = default);
}
