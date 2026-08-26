using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ClubPay.Agent.Core.Services;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.ViewModels;

/// <summary>
/// Purely passive: shows PC/club/zone info, a clock and a QR code that links to the payment page.
/// Everything else (starting a session) happens only via a Controller start_session command through
/// ISessionCoordinator — this view has no local decision-making of its own.
/// </summary>
public partial class LockScreenViewModel : ObservableObject
{
    private readonly IAgentService _agent;
    private readonly QrCodeService _qr;
    private readonly DispatcherTimer _clock;

    [ObservableProperty] private string _pcId = "PC-12";
    [ObservableProperty] private string _zoneLabel = "Standard Zone · Standart Zona";
    [ObservableProperty] private string _clubName = "NEXUS ARENA";
    [ObservableProperty] private string _currentTime = DateTime.Now.ToString("HH:mm");

    [ObservableProperty] private BitmapImage? _payQrImage;
    [ObservableProperty] private BitmapImage? _wifiQrImage;

    public LockScreenViewModel(IAgentService agent, QrCodeService qr)
    {
        _agent = agent;
        _qr = qr;

        UpdateIdentity();

        _clock = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => CurrentTime = DateTime.Now.ToString("HH:mm");
        _clock.Start();

        _agent.StaticPaymentQrUrlChanged += RefreshPaymentQr;
        _agent.BootstrapChanged += RefreshIdentity;
        GenerateQrCodes();
    }

    private void GenerateQrCodes()
    {
        PayQrImage = string.IsNullOrWhiteSpace(_agent.StaticPaymentQrUrl)
            ? null
            : _qr.Generate(_agent.StaticPaymentQrUrl, 300);
        WifiQrImage = _qr.GenerateWifi(_agent.WifiSsid, _agent.WifiPassword, pixelSize: 108);
    }

    private void RefreshPaymentQr()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            GenerateQrCodes();
        else
            _ = dispatcher.InvokeAsync(GenerateQrCodes);
    }

    private void RefreshIdentity()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            UpdateIdentity();
        else
            _ = dispatcher.InvokeAsync(UpdateIdentity);
    }

    private void UpdateIdentity()
    {
        PcId = _agent.PcId;
        ClubName = _agent.ClubName;
        ZoneLabel = _agent.ZoneName;
    }

    /// <summary>Called by MainViewModel whenever the coordinator reports a fresh transition to Locked.</summary>
    public void Reset() => GenerateQrCodes();

}
