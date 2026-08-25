using System.Windows;
using ClubPay.Agent.Client.Services;

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
            MessageBox.Show(L("PhoneRequired"), "ClubPay",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!RecipientConsent)
        {
            MessageBox.Show(L("ConsentRequired"), "ClubPay",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private static string L(string key)
        => Application.Current?.TryFindResource("Loc") is LocalizationService localizer
            ? localizer[key]
            : key;
}
