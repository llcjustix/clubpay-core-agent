using System.Windows;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.Views;

public partial class EndSessionDialog : Window
{
    // A signed-in player is resolved by Core from the session itself. Guest sessions still use
    // the server's voucher fallback, without forcing a phone prompt at the end of a game.
    public string RecipientPhone => string.Empty;
    public bool RecipientConsent => false;

    public EndSessionDialog() => InitializeComponent();

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private static string L(string key)
        => Application.Current?.TryFindResource("Loc") is LocalizationService localizer
            ? localizer[key]
            : key;
}
