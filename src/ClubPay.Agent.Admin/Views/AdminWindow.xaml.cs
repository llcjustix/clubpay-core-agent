using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.Core;

namespace ClubPay.Agent.Admin.Views;

public partial class AdminWindow : Window
{
    private readonly Uri _cloudAdminUri;
    private readonly Uri _cloudHealthUri;
    private readonly IReadOnlyList<Uri> _localAdminUris;
    private Uri _adminUri;
    private bool _usingLocalController;
    private bool _fallbackAttempted;

    public AdminWindow(IConfiguration configuration)
    {
        InitializeComponent();
        _cloudAdminUri = ParseUri(configuration["Manager:CloudAdminUrl"] ?? configuration["Manager:AdminUrl"], new Uri("https://clubpay.justix.uz/admin"));
        _cloudHealthUri = ParseUri(configuration["Manager:CloudHealthUrl"], new Uri("https://api-clubpay.justix.uz/api/node/status"));
        _localAdminUris = ReadLocalControllerUris(configuration);
        _adminUri = _cloudAdminUri;
        Loaded += AdminWindow_Loaded;
    }

    private async void AdminWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ManagerWebView.EnsureCoreWebView2Async();
            ManagerWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            ManagerWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            await ResolveAndNavigateAsync();
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

    private async Task ResolveAndNavigateAsync()
    {
        if (await IsHealthyAsync(_cloudHealthUri))
        {
            _adminUri = _cloudAdminUri;
            _usingLocalController = false;
            Navigate();
            return;
        }

        foreach (var localUri in _localAdminUris)
        {
            if (!await IsHealthyAsync(new Uri(localUri, "/api/node/status")))
                continue;

            _adminUri = localUri;
            _usingLocalController = true;
            Navigate();
            return;
        }

        _adminUri = _cloudAdminUri;
        _usingLocalController = false;
        Navigate();
    }

    private void Navigate()
    {
        UnavailableOverlay.Visibility = Visibility.Collapsed;
        ManagerWebView.CoreWebView2.Navigate(_adminUri.AbsoluteUri);
    }

    private void ManagerWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            return;
        }
        if (!_usingLocalController && !_fallbackAttempted && _localAdminUris.Count > 0)
        {
            _fallbackAttempted = true;
            _ = ResolveAndNavigateAsync();
            return;
        }
        ShowUnavailable("Cloud и локальный Controller недоступны. Проверьте сеть клуба и сервис ClubPay Controller Node.");
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (ManagerWebView.CoreWebView2 is null)
        {
            return;
        }
        _fallbackAttempted = false;
        _ = ResolveAndNavigateAsync();
    }

    private void ShowUnavailable(string message)
    {
        UnavailableMessage.Text = message;
        UnavailableOverlay.Visibility = Visibility.Visible;
    }

    private static Uri ParseUri(string? value, Uri fallback) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? parsed
            : fallback;

    private static IReadOnlyList<Uri> ReadLocalControllerUris(IConfiguration configuration)
    {
        var result = new List<Uri>();
        AddUri(result, configuration["Manager:LocalControllerUrl"]);
        foreach (var child in configuration.GetSection("Manager:LocalControllerUrls").GetChildren())
            AddUri(result, child.Value);
        foreach (var value in (configuration["Manager:LocalControllerUrls"] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            AddUri(result, value);
        return result;
    }

    private static void AddUri(ICollection<Uri> target, string? value)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
            !target.Any(existing => existing == uri))
        {
            target.Add(uri);
        }
    }

    private static async Task<bool> IsHealthyAsync(Uri uri)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync(uri);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
