using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;
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
    private readonly IAgentService _agent;
    private readonly DispatcherTimer _timer;
    private DateTime _untilUtc;

    [ObservableProperty] private string _graceTimeText = "10:00";
    [ObservableProperty] private int _graceRemainingSeconds = Constants.Timer.GracePeriod;
    [ObservableProperty] private double _graceBarWidth = 520;

    [ObservableProperty] private BitmapImage? _extendQrImage;

    public FreezeViewModel(QrCodeService qr, IAgentService agent)
    {
        _qr = qr;
        _agent = agent;
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
    }

    /// <summary>untilUtc is the grace deadline the coordinator computed; session (if any) makes the
    /// QR session-bound so a photographed/replayed code can't be reused on another PC/session.</summary>
    public void ShowGrace(DateTime untilUtc, Session? session = null)
    {
        _untilUtc = untilUtc;

        var extendUrl = QrUrlBuilder.BuildSessionUrl(_agent.PaymentBaseUrl, _agent.ExternalPcId, session?.CoreSessionId, session?.Id);
        ExtendQrImage = _qr.Generate(extendUrl, 320);

        Refresh();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Refresh()
    {
        int remaining = (int)Math.Max(0, (_untilUtc - DateTime.UtcNow).TotalSeconds);
        GraceRemainingSeconds = remaining;
        GraceTimeText = FormatTime(remaining);
        GraceBarWidth = 520.0 * remaining / Constants.Timer.GracePeriod;

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
