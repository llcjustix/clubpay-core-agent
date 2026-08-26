using System.Windows;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.Views;

public partial class EndSessionDialog : Window
{
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
