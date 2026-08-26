using System.Windows;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.Views;

public partial class VoucherDeliveryDialog : Window
{
    public VoucherDeliveryDialog(ClientSessionEndResult result, QrCodeService qr)
    {
        InitializeComponent();
        if (result.ProfileBalanceAddedSeconds > 0)
        {
            TitleText.Text = L("ProfileTimeSaved");
            VoucherCodeText.Text = string.Empty;
            MessageText.Text = string.Format(L("ProfileTimeSavedDescription"), FormatDuration(result.ProfileBalanceAddedSeconds));
            QrBorder.Visibility = Visibility.Collapsed;
            TelegramBotText.Visibility = Visibility.Collapsed;
            return;
        }
        if (result.VoucherSeconds <= 0 && string.IsNullOrWhiteSpace(result.VoucherCode))
        {
            TitleText.Text = L("SessionCompleted");
            VoucherCodeText.Text = string.Empty;
            MessageText.Text = L("NoTimeRemaining");
            QrBorder.Visibility = Visibility.Collapsed;
            TelegramBotText.Visibility = Visibility.Collapsed;
            return;
        }
        VoucherCodeText.Text = string.IsNullOrWhiteSpace(result.VoucherCode) ? string.Empty : $"{L("Voucher")}: {result.VoucherCode}";

        if (result.DeliveryStatus == "sent")
        {
            MessageText.Text = L("VoucherSent");
            QrBorder.Visibility = Visibility.Collapsed;
            TelegramBotText.Visibility = Visibility.Collapsed;
        }
        else if (!string.IsNullOrWhiteSpace(result.TelegramLink))
        {
            var username = GetBotUsername(result);
            TitleText.Text = L("GetVoucherInTelegram");
            MessageText.Text = L("OpenTelegramBotAndStart");
            TelegramBotText.Text = string.IsNullOrWhiteSpace(username)
                ? ""
                : $"{L("FindTelegramBot")}: @{username}";
            TelegramBotText.Visibility = string.IsNullOrWhiteSpace(username)
                ? Visibility.Collapsed
                : Visibility.Visible;
            QrImage.Source = qr.Generate(result.TelegramLink, 210);
        }
        else
        {
            MessageText.Text = result.DeliveryStatus == "telegram_not_configured"
                ? L("VoucherBotNotConfigured")
                : L("VoucherNotDelivered");
            QrBorder.Visibility = Visibility.Collapsed;
            TelegramBotText.Visibility = Visibility.Collapsed;
        }
    }

    private static string? GetBotUsername(ClientSessionEndResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.TelegramBotUsername))
            return result.TelegramBotUsername.Trim().TrimStart('@');
        if (!Uri.TryCreate(result.TelegramLink, UriKind.Absolute, out var link)
            || !string.Equals(link.Host, "t.me", StringComparison.OrdinalIgnoreCase))
            return null;
        return link.AbsolutePath.Trim('/');
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static string FormatDuration(int seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");

    private static string L(string key)
        => Application.Current?.TryFindResource("Loc") is LocalizationService localizer
            ? localizer[key]
            : key;
}
