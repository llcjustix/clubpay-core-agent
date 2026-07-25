using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.ViewModels;

/// <summary>
/// Shows PC/club/zone info, a clock and a QR code that links to the payment page, plus the ТЗ §7
/// single code-entry field (revealed by Enter) for vouchers and manager master codes. All code
/// verification lives in ILockCodeService — this ViewModel only holds UI state and maps the
/// rejection reason to a friendly message.
/// </summary>
public partial class LockScreenViewModel : ObservableObject
{
    private readonly IAgentService _agent;
    private readonly QrCodeService _qr;
    private readonly ILockCodeService _lockCodes;
    private readonly ILogger<LockScreenViewModel> _logger;
    private readonly DispatcherTimer _clock;

    [ObservableProperty] private string _pcId = string.Empty;
    [ObservableProperty] private string _zoneLabel = string.Empty;
    [ObservableProperty] private string _clubName = string.Empty;
    [ObservableProperty] private string _currentTime = DateTime.Now.ToString("HH:mm");

    [ObservableProperty] private BitmapImage? _payQrImage;
    [ObservableProperty] private BitmapImage? _wifiQrImage;

    [ObservableProperty] private bool _isInputVisible;
    [ObservableProperty] private string _codeInput = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isSubmitting;

    public bool HasError => ErrorMessage.Length > 0;

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    public LockScreenViewModel(
        IAgentService agent,
        QrCodeService qr,
        ILockCodeService lockCodes,
        ILogger<LockScreenViewModel> logger)
    {
        _agent = agent;
        _qr = qr;
        _lockCodes = lockCodes;
        _logger = logger;

        PcId = agent.PcId;
        ClubName = agent.ClubName;
        ZoneLabel = ZoneLabelFor(agent.Zone);

        _clock = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => CurrentTime = DateTime.Now.ToString("HH:mm");
        _clock.Start();

        GenerateQrCodes();
    }

    [RelayCommand]
    private void RevealInput()
    {
        CodeInput = string.Empty;
        ErrorMessage = string.Empty;
        IsInputVisible = true;
    }

    [RelayCommand]
    private void HideInput()
    {
        IsInputVisible = false;
        CodeInput = string.Empty;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SubmitCodeAsync(CancellationToken ct)
    {
        var code = CodeInput.Trim();
        if (code.Length == 0 || IsSubmitting)
            return;

        IsSubmitting = true;
        try
        {
            var result = await _lockCodes.SubmitAsync(code, ct);
            if (result.Accepted)
            {
                // The coordinator's StateChanged flips the whole screen away from Locked.
                HideInput();
                return;
            }

            ErrorMessage = LockCodeMessages.For(result.RejectionReason);
        }
        catch (OperationCanceledException)
        {
            // App shutdown — nothing to show.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LockScreen kodini yuborishda kutilmagan xato");
            ErrorMessage = LockCodeMessages.GenericError;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private void GenerateQrCodes()
    {
        var payUrl = QrUrlBuilder.BuildLockScreenUrl(_agent.PaymentBaseUrl, _agent.ExternalPcId);
        PayQrImage = _qr.Generate(payUrl, 300);
        WifiQrImage = _qr.GenerateWifi(_agent.WifiSsid, _agent.WifiPassword, pixelSize: 108);
    }

    /// <summary>Called by MainViewModel whenever the coordinator reports a fresh transition to Locked.</summary>
    public void Reset()
    {
        HideInput();
        GenerateQrCodes();
    }

    private static string ZoneLabelFor(ZoneType z) => z switch
    {
        ZoneType.Pro => "Pro Zone · Pro Zona",
        ZoneType.Vip => "VIP Zone · VIP Zona",
        _ => "Standard Zone · Standart Zona"
    };
}
