using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.ViewModels;

/// <summary>
/// Purely passive grace-period display: shows the countdown and a session-bound payment QR.
/// ISessionCoordinator alone decides when grace starts and expires — this view only renders
/// <see cref="ShowGrace"/>'s deadline; resuming happens only via a Controller extend_session command.
/// </summary>
public partial class FreezeViewModel : ObservableObject
{
    private readonly QrCodeService _qr;
    private readonly DispatcherTimer _timer;
    private DateTime _untilUtc;
    private int _totalGraceSeconds = Constants.Timer.GracePeriod;

    [ObservableProperty] private string _graceTimeText = "03:00";
    [ObservableProperty] private int _graceRemainingSeconds = Constants.Timer.GracePeriod;
    [ObservableProperty] private double _graceBarWidth = 520;

    [ObservableProperty] private BitmapImage? _extendQrImage;

    public FreezeViewModel(QrCodeService qr)
    {
        _qr = qr;
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
    }

    /// <summary>untilUtc is the grace deadline the coordinator computed. The URL itself is supplied
    /// by Core and is bound to this specific session, so the Agent never creates a reusable URL.</summary>
    public void ShowGrace(DateTime untilUtc, Session? session = null)
    {
        _untilUtc = untilUtc;
        _totalGraceSeconds = Math.Max(1, session?.GraceSeconds ?? Constants.Timer.GracePeriod);

        ExtendQrImage = string.IsNullOrWhiteSpace(session?.ExtendUrl)
            ? null
            : _qr.Generate(session.ExtendUrl, 320);

        Refresh();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        ExtendQrImage = null;
    }

    private void Refresh()
    {
        int remaining = (int)Math.Max(0, (_untilUtc - DateTime.UtcNow).TotalSeconds);
        GraceRemainingSeconds = remaining;
        GraceTimeText = FormatTime(remaining);
        GraceBarWidth = 520.0 * remaining / _totalGraceSeconds;

        if (remaining == 0)
            _timer.Stop();
    }

    private static string FormatTime(int secs)
    {
        int m = secs / 60;
        int s = secs % 60;
        return $"{m:D2}:{s:D2}";
    }
}
