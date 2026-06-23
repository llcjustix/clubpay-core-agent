using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.ViewModels;

public partial class LockScreenViewModel : ObservableObject
{
    private readonly IAgentService _agent;
    private readonly QrCodeService _qr;
    private readonly IVoucherService _voucher;
    private readonly DispatcherTimer _clock;

    public event Action<Session>? SessionRequested;

    [ObservableProperty] private string _pcId = "PC-12";
    [ObservableProperty] private string _zoneLabel = "Standard Zone · Standart Zona";
    [ObservableProperty] private string _clubName = "NEXUS ARENA";
    [ObservableProperty] private string _currentTime = DateTime.Now.ToString("HH:mm");

    [ObservableProperty] private BitmapImage? _payQrImage;
    [ObservableProperty] private BitmapImage? _wifiQrImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCode))]
    private bool _isCodeInputVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCode))]
    private string _codeInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCodeError))]
    private string? _codeError;

    public bool HasCode => CodeInput.Length > 0;
    public bool HasCodeError => !string.IsNullOrEmpty(CodeError);

    public LockScreenViewModel(IAgentService agent, QrCodeService qr, IVoucherService voucher)
    {
        _agent = agent;
        _qr = qr;
        _voucher = voucher;

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
        WifiQrImage = _qr.GenerateWifi(_agent.WifiSsid, pixelSize: 108);
    }

    public void Reset()
    {
        IsCodeInputVisible = false;
        CodeInput = string.Empty;
        CodeError = null;
        GenerateQrCodes();
    }

    [RelayCommand]
    public void ToggleCodeInput()
    {
        IsCodeInputVisible = !IsCodeInputVisible;
        if (!IsCodeInputVisible)
        {
            CodeInput = string.Empty;
            CodeError = null;
        }
    }

    [RelayCommand]
    public void AppendKey(string key)
    {
        if (!IsCodeInputVisible) { IsCodeInputVisible = true; }
        if (key == "\b")
        {
            if (CodeInput.Length > 0)
                CodeInput = CodeInput[..^1];
        }
        else if (CodeInput.Length < 9)
        {
            CodeInput += key.ToUpper();
        }
        CodeError = null;
    }

    [RelayCommand]
    public async Task SubmitCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(CodeInput)) return;

        var rawCode = CodeInput.Replace("-", "");
        var token = _voucher.Redeem(rawCode, PcId);

        if (token is null || !_voucher.Validate(token, PcId, DateTime.UtcNow))
        {
            CodeError = "Kod noto'g'ri yoki muddati o'tgan";
            CodeInput = string.Empty;
            return;
        }

        var tariff = new Tariff(Guid.NewGuid(), "Voucher", ClubPay.Agent.Core.Models.ZoneType.Standard,
                                 token.RemainingSeconds / 60, 0);
        var session = new Session(Guid.NewGuid(), PcId, tariff, DateTime.UtcNow, token.RemainingSeconds);

        await _agent.StartSessionAsync(session);
        IsCodeInputVisible = false;
        CodeInput = string.Empty;
        SessionRequested?.Invoke(session);
    }

    private static string ZoneLabelFor(ZoneType z) => z switch
    {
        ZoneType.Pro => "Pro Zone · Pro Zona",
        ZoneType.Vip => "VIP Zone · VIP Zona",
        _ => "Standard Zone · Standart Zona"
    };
}
