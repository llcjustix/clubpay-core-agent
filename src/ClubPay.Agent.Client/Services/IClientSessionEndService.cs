namespace ClubPay.Agent.Client.Services;

public sealed record ClientSessionEndResult(
    string? VoucherCode,
    int VoucherSeconds,
    string DeliveryStatus,
    string? TelegramLink,
    string? TelegramBotUsername);

/// <summary>
/// Ends the current kiosk session through Core. Core remains the authority: it commands the Agent
/// to lock, creates a voucher for unused time, and delivers it through the existing Telegram flow.
/// </summary>
public interface IClientSessionEndService
{
    Task<ClientSessionEndResult> EndCurrentSessionAsync(
        string recipientPhone,
        bool recipientConsent,
        CancellationToken ct = default);
}
