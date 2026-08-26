namespace ClubPay.Agent.Client.Services;

public sealed record ClientSessionEndResult(
    string? VoucherCode,
    int VoucherSeconds,
    int ProfileBalanceAddedSeconds,
    string DeliveryStatus,
    string? TelegramLink,
    string? TelegramBotUsername);

/// <summary>
/// Ends the current kiosk session through Core. Core remains the authority: it commands the Agent
/// to lock, saves unused time to a signed-in player's balance, and falls back to a voucher for guests.
/// </summary>
public interface IClientSessionEndService
{
    Task<ClientSessionEndResult> EndCurrentSessionAsync(
        string recipientPhone,
        bool recipientConsent,
        CancellationToken ct = default);
}
