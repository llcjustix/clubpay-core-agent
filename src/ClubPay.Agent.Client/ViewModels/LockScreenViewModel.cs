using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ClubPay.Agent.Core.Models;
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

        PcId = agent.PcId;
        ClubName = agent.ClubName;
        ZoneLabel = ZoneLabelFor(agent.Zone);

        _clock = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => CurrentTime = DateTime.Now.ToString("HH:mm");
        _clock.Start();

        GenerateQrCodes();
    }

    private void GenerateQrCodes()
    {
        var payUrl = $"https://pay.clubpay.uz/?pc={Uri.EscapeDataString(PcId)}";
        PayQrImage = _qr.Generate(payUrl, 300);
        WifiQrImage = _qr.GenerateWifi(_agent.WifiSsid, _agent.WifiPassword, pixelSize: 108);
    }

    /// <summary>Called by MainViewModel whenever the coordinator reports a fresh transition to Locked.</summary>
    public void Reset() => GenerateQrCodes();

    private static string ZoneLabelFor(ZoneType z) => z switch
    {
        ZoneType.Pro => "Pro Zone · Pro Zona",
        ZoneType.Vip => "VIP Zone · VIP Zona",
        _ => "Standard Zone · Standart Zona"
    };
}
