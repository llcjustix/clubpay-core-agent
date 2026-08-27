using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.ViewModels;

/// <summary>
/// Purely passive session display: refreshes the countdown text/warn banners every second from
/// whatever ISessionCoordinator reports. It never decides expiry itself — that is the coordinator's
/// job (it raises StateChanged, MainViewModel calls Sync/Stop accordingly).
/// </summary>
public partial class ActiveSessionViewModel : ObservableObject
{
    private readonly QrCodeService _qr;
    private readonly IAgentService _agent;
    private readonly DispatcherTimer _timer;
    private Session? _session;
    private string? _extendUrl;

    [ObservableProperty] private string _remainingTimeText = "01:24";
    [ObservableProperty] private int _remainingSeconds = 5075;
    [ObservableProperty] private string _clubName = "ClubPay";
    [ObservableProperty] private string _zoneLabel = "";
    [ObservableProperty] private BitmapImage? _extendQrImage;

    public ActiveSessionViewModel(QrCodeService qr, IAgentService agent)
    {
        _qr = qr;
        _agent = agent;
        RefreshIdentity();
        _agent.BootstrapChanged += RefreshIdentity;
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => RefreshTime(DateTime.UtcNow);
    }

    /// <summary>Called by MainViewModel whenever the coordinator reports an Active session — on
    /// start, on extend, or on any other StateChanged that leaves the session Active. Cheap to call
    /// repeatedly; only regenerates the QR/labels when the session identity actually changed.</summary>
    public void Sync(Session session)
    {
        bool isNewSession = _session is null || _session.Id != session.Id;
        bool hasNewExtendUrl = !string.Equals(_extendUrl, session.ExtendUrl, StringComparison.Ordinal);
        _session = session;

        if (isNewSession)
        {
            ZoneLabel = string.IsNullOrWhiteSpace(session.Zone) ? _agent.ZoneName : session.Zone;

        }

        if (hasNewExtendUrl)
        {
            _extendUrl = session.ExtendUrl;
            ExtendQrImage = string.IsNullOrWhiteSpace(session.ExtendUrl)
                ? null
                : _qr.Generate(session.ExtendUrl, 116);
        }

        RefreshTime(DateTime.UtcNow);
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _session = null;
        _extendUrl = null;
        ExtendQrImage = null;
    }

    private void RefreshTime(DateTime now)
    {
        if (_session is null)
            return;

        int rem = _session.RemainingSeconds(now);
        RemainingSeconds = rem;
        RemainingTimeText = FormatTime(rem);

    }

    private void RefreshIdentity()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(RefreshIdentity);
            return;
        }
        ClubName = _agent.ClubName;
        if (_session is null || string.IsNullOrWhiteSpace(_session.Zone))
            ZoneLabel = _agent.ZoneName;
    }

    private static string FormatTime(int totalSeconds)
    {
        int safeSeconds = Math.Max(0, totalSeconds);
        int totalMinutes = safeSeconds / 60;
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        if (safeSeconds <= (int)TimeSpan.FromDays(1).TotalSeconds)
            return $"{hours:D2}:{minutes:D2}";

        int days = hours / 24;
        return $"{days:D2}:{hours % 24:D2}:{minutes:D2}";
    }

}
