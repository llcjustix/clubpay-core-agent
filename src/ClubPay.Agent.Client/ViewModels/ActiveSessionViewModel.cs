using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
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
    private readonly DispatcherTimer _timer;
    private Session? _session;
    private int _previousRemainingSeconds = -1;
    private int _toastTicksRemaining;

    [ObservableProperty] private string _remainingTimeText = "01:24:35";
    [ObservableProperty] private int _remainingSeconds = 5075;
    [ObservableProperty] private string _zoneLabel = "Standard Zone";
    [ObservableProperty] private string _zoneLabelUz = "Standart Zona";
    [ObservableProperty] private string _tariffLabel = "2 soat";
    [ObservableProperty] private string _playedHoursText = "2 soat";
    [ObservableProperty] private BitmapImage? _extendQrImage;

    // Warning ladder (ТЗ §7) — bound in ActiveSessionView, mutually exclusive priority:
    // banner (1 daq) > toast (5 daq, o'z-o'zidan yopiladi) > soft 10-daq karta.
    [ObservableProperty] private bool _isAnyWarningVisible;
    [ObservableProperty] private bool _isSoft10Visible;
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private bool _isBannerVisible;

    public ActiveSessionViewModel(QrCodeService qr)
    {
        _qr = qr;
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
        _session = session;

        if (isNewSession)
        {
            ZoneLabel = ZoneLabelFor(session.Tariff.Zone, false);
            ZoneLabelUz = ZoneLabelFor(session.Tariff.Zone, true);
            TariffLabel = session.Tariff.DurationLabel;

            var extendUrl = $"https://pay.clubpay.uz/?session={session.Id:N}";
            ExtendQrImage = _qr.Generate(extendUrl, 116);
        }

        RefreshTime(DateTime.UtcNow);
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _session = null;
        _previousRemainingSeconds = -1;
        _toastTicksRemaining = 0;
        IsAnyWarningVisible = false;
        IsSoft10Visible = false;
        IsToastVisible = false;
        IsBannerVisible = false;
    }

    private void RefreshTime(DateTime now)
    {
        if (_session is null)
            return;

        int rem = _session.RemainingSeconds(now);
        RemainingSeconds = rem;
        RemainingTimeText = FormatTime(rem);

        int played = _session.ElapsedSeconds(now) / 3600;
        PlayedHoursText = played > 0 ? $"{played} soat" : "yangi sessiya";

        IsBannerVisible = SessionWarningCalculator.IsBannerVisible(rem);
        IsAnyWarningVisible = SessionWarningCalculator.IsAnyWarningVisible(rem);

        if (SessionWarningCalculator.ShouldShowToast(_previousRemainingSeconds, rem))
        {
            IsToastVisible = true;
            _toastTicksRemaining = Constants.Timer.ToastDurationSeconds;
        }
        else if (IsToastVisible)
        {
            _toastTicksRemaining--;
            if (_toastTicksRemaining <= 0 || IsBannerVisible)
                IsToastVisible = false;
        }

        IsSoft10Visible = IsAnyWarningVisible && !IsToastVisible && !IsBannerVisible;
        _previousRemainingSeconds = rem;
    }

    private static string FormatTime(int totalSeconds)
    {
        int h = totalSeconds / 3600;
        int m = (totalSeconds % 3600) / 60;
        int s = totalSeconds % 60;
        return h > 0 ? $"{h:D2}:{m:D2}:{s:D2}" : $"{m:D2}:{s:D2}";
    }

    private static string ZoneLabelFor(ZoneType zone, bool uz) => (zone, uz) switch
    {
        (ZoneType.Pro, false) => "Pro Zone",
        (ZoneType.Pro, true) => "Pro Zona",
        (ZoneType.Vip, false) => "VIP Zone",
        (ZoneType.Vip, true) => "VIP Zona",
        (_, false) => "Standard Zone",
        _ => "Standart Zona",
    };
}
