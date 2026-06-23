using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClubPay.Agent.Core;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.ViewModels;

public partial class FreezeViewModel : ObservableObject
{
    private readonly QrCodeService _qr;
    private readonly DispatcherTimer _timer;
    private int _totalGraceSeconds = Constants.Timer.GracePeriod;

    public event Action<int>? ResumeRequested;
    public event Action? ExpiredRequested;

    [ObservableProperty] private string _graceTimeText = "10:00";
    [ObservableProperty] private int _graceRemainingSeconds = Constants.Timer.GracePeriod;
    [ObservableProperty] private double _graceBarWidth = 520;

    [ObservableProperty] private string _upsellLine1 = "You've played — continue at a discount";
    [ObservableProperty] private string _upsellLine1Uz = "Chegirma bilan davom eting";
    [ObservableProperty] private long _discountTiyin = 1_300_000;  // 13 000 so'm
    [ObservableProperty] private long _originalTiyin = 1_500_000;  // 15 000 so'm

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVoucherError))]
    private string _voucherInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVoucherError))]
    private string? _voucherError;

    public bool HasVoucherError => !string.IsNullOrEmpty(VoucherError);

    [ObservableProperty] private BitmapImage? _extendQrImage;

    public FreezeViewModel(QrCodeService qr)
    {
        _qr = qr;
        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    public void StartGrace(int playedHours = 2)
    {
        _totalGraceSeconds = Constants.Timer.GracePeriod;
        GraceRemainingSeconds = _totalGraceSeconds;
        GraceTimeText = FormatTime(_totalGraceSeconds);
        GraceBarWidth = 520;
        VoucherInput = string.Empty;
        VoucherError = null;

        UpsellLine1 = $"You've played {playedHours} hour{(playedHours != 1 ? "s" : "")} — continue at a discount";
        UpsellLine1Uz = $"Siz {playedHours} soat o'ynadingiz — chegirma bilan davom eting";

        var extendUrl = "https://pay.clubpay.uz/?extend=1";
        ExtendQrImage = _qr.Generate(extendUrl, 320);

        _timer.Start();
    }

    private void OnTick(object? _, EventArgs __)
    {
        GraceRemainingSeconds = Math.Max(0, GraceRemainingSeconds - 1);
        GraceTimeText = FormatTime(GraceRemainingSeconds);
        GraceBarWidth = 520.0 * GraceRemainingSeconds / _totalGraceSeconds;

        if (GraceRemainingSeconds == 0)
        {
            _timer.Stop();
            ExpiredRequested?.Invoke();
        }
    }

    [RelayCommand]
    public void AppendVoucher(string key)
    {
        if (key == "\b")
        {
            if (VoucherInput.Length > 0)
                VoucherInput = VoucherInput[..^1];
        }
        else if (VoucherInput.Length < 9)
        {
            VoucherInput += key.ToUpper();
        }
        VoucherError = null;
    }

    [RelayCommand]
    public void SubmitVoucher()
    {
        if (string.IsNullOrWhiteSpace(VoucherInput))
        {
            VoucherError = "Kodni kiriting";
            return;
        }
        // Real validation via IVoucherService — wired in real agent
        _timer.Stop();
        ResumeRequested?.Invoke(3600);  // demo: 1 hr extension
    }

    private static string FormatTime(int secs)
    {
        int m = secs / 60;
        int s = secs % 60;
        return $"{m:D2}:{s:D2}";
    }
}
