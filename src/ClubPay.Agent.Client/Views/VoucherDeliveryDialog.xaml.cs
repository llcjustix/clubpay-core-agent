using System.Windows;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.Views;

public partial class VoucherDeliveryDialog : Window
{
    public VoucherDeliveryDialog(ClientSessionEndResult result, QrCodeService qr)
    {
        InitializeComponent();
        VoucherCodeText.Text = string.IsNullOrWhiteSpace(result.VoucherCode) ? string.Empty : $"Ваучер: {result.VoucherCode}";

        if (result.DeliveryStatus == "sent")
        {
            MessageText.Text = "Ваучер отправлен в Telegram. Проверьте сообщения.";
            QrBorder.Visibility = Visibility.Collapsed;
            TelegramBotText.Visibility = Visibility.Collapsed;
        }
        else if (!string.IsNullOrWhiteSpace(result.TelegramLink))
        {
            var username = GetBotUsername(result);
            TitleText.Text = "Получите ваучер в Telegram";
            MessageText.Text = "Отсканируйте QR, откройте бота и нажмите Start. После привязки номера ваучер придёт автоматически.";
            TelegramBotText.Text = string.IsNullOrWhiteSpace(username)
                ? ""
                : $"Или найдите в Telegram: @{username}";
            TelegramBotText.Visibility = string.IsNullOrWhiteSpace(username)
                ? Visibility.Collapsed
                : Visibility.Visible;
            QrImage.Source = qr.Generate(result.TelegramLink, 210);
        }
        else
        {
            MessageText.Text = result.DeliveryStatus == "telegram_not_configured"
                ? "Ваучер создан, но Telegram-бот пока не настроен. Сохраните код и обратитесь к администратору."
                : "Ваучер создан. Если он не пришёл в Telegram, обратитесь к администратору.";
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
}
