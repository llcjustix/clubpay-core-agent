using System.Windows;

namespace ClubPay.Agent.Client.Views;

public partial class EndSessionDialog : Window
{
    public string RecipientPhone => PhoneBox.Text.Trim();
    public bool RecipientConsent => ConsentCheck.IsChecked == true;

    public EndSessionDialog() => InitializeComponent();

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RecipientPhone) || RecipientPhone == "+998")
        {
            MessageBox.Show("Укажите номер телефона для отправки ваучера в Telegram.", "ClubPay",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!RecipientConsent)
        {
            MessageBox.Show("Нужно согласие на хранение номера и отправку ваучера в Telegram.", "ClubPay",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
}
