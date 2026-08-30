using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;

namespace ClubPay.Agent.Admin.Views;

public partial class AdminWindow : Window
{
    private readonly Uri _adminUri;

    public AdminWindow(IConfiguration configuration)
    {
        InitializeComponent();
        var address = configuration["Manager:AdminUrl"]?.Trim();
        if (!Uri.TryCreate(address, UriKind.Absolute, out var adminUri) || adminUri.Scheme != Uri.UriSchemeHttps)
        {
            adminUri = new Uri("https://clubpay.justix.uz/admin");
        }
        _adminUri = adminUri;
        Loaded += AdminWindow_Loaded;
    }

    private async void AdminWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ManagerWebView.EnsureCoreWebView2Async();
            ManagerWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            ManagerWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Navigate();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowUnavailable("На этом ПК нет Microsoft Edge WebView2 Runtime. Установите его один раз: https://developer.microsoft.com/microsoft-edge/webview2/");
        }
        catch (Exception ex)
        {
            ShowUnavailable("Не удалось запустить встроенную админку: " + ex.Message);
        }
    }

    private void Navigate()
    {
        UnavailableOverlay.Visibility = Visibility.Collapsed;
        ConnectionStatus.Text = "Подключение к ClubPay…";
        ManagerWebView.CoreWebView2.Navigate(_adminUri.AbsoluteUri);
    }

    private void ManagerWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            ConnectionStatus.Text = "Подключено";
            return;
        }
        ShowUnavailable("Сервер ClubPay недоступен. Проверьте интернет и адрес панели.");
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (ManagerWebView.CoreWebView2 is null)
        {
            return;
        }
        Navigate();
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(_adminUri.AbsoluteUri) { UseShellExecute = true });
    }

    private void ShowUnavailable(string message)
    {
        ConnectionStatus.Text = "Нет подключения";
        UnavailableMessage.Text = message;
        UnavailableOverlay.Visibility = Visibility.Visible;
    }
}
