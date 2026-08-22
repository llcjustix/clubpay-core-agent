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
        }
        else if (!string.IsNullOrWhiteSpace(result.TelegramLink))
        {
            TitleText.Text = "Получите ваучер в Telegram";
            MessageText.Text = "Отсканируйте QR, откройте бота и нажмите Start. После привязки номера ваучер придёт автоматически.";
            QrImage.Source = qr.Generate(result.TelegramLink, 210);
        }
        else
        {
            MessageText.Text = "Ваучер создан. Если он не пришёл в Telegram, обратитесь к администратору.";
            QrBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
