using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Admin.ViewModels;

public partial class CashPaymentViewModel : ObservableObject
{
    private static readonly Dictionary<string, int> TariffDurationSeconds = new()
    {
        ["30 daqiqa"] = 30 * 60,
        ["1 soat"] = 60 * 60,
        ["2 soat"] = 2 * 60 * 60,
        ["3 soat"] = 3 * 60 * 60,
        ["5 soat"] = 5 * 60 * 60,
    };

    private readonly ICashAuditService _cashAudit;
    private readonly IManagerPinService _pinService;
    private readonly ILogger<CashPaymentViewModel> _logger;

    [ObservableProperty] private string _managerId = "unknown";
    [ObservableProperty] private string _pcId = "PC-12";
    [ObservableProperty] private string _zoneLabel = "Pro Zone";
    [ObservableProperty] private string _selectedTariff = "2 soat";
    [ObservableProperty] private long _amountTiyin = 2_800_000;
    [ObservableProperty] private string _reasonCode = "Internet yo'q";
    [ObservableProperty] private string _pinInput = string.Empty;
    [ObservableProperty] private bool _isPinVisible = false;

    public event Action? ConfirmRequested;
    public event Action? CancelRequested;

    public string[] ReasonCodes { get; } = ["Internet yo'q", "Mijoz iltimosi", "Boshqa"];
    public string[] Tariffs { get; } = ["30 daqiqa", "1 soat", "2 soat", "3 soat", "5 soat"];

    public string AmountLabel => Constants.Money.FormatSom(AmountTiyin);

    public CashPaymentViewModel(ICashAuditService cashAudit, IManagerPinService pinService, ILogger<CashPaymentViewModel> logger)
    {
        _cashAudit = cashAudit;
        _pinService = pinService;
        _logger = logger;
    }

    public void Load(PcCard pc)
    {
        PcId = pc.PcId;
        ZoneLabel = pc.ZoneLabel;
        PinInput = string.Empty;
        ReasonCode = ReasonCodes[0];
    }

    [RelayCommand]
    public void AppendPin(string key)
    {
        if (key == "\b")
        {
            if (PinInput.Length > 0)
                PinInput = PinInput[..^1];
        }
        else if (PinInput.Length < 4 && char.IsDigit(key[0]))
        {
            PinInput += key;
        }
    }

    [RelayCommand]
    public async Task ConfirmAsync()
    {
        if (!_pinService.Verify(PinInput))
        {
            _logger.LogWarning("Naqd to'lov rad etildi: noto'g'ri PIN (PC={PcId})", PcId);
            return;
        }

        var entry = new CashAuditEntry(
            ManagerId,
            PcId,
            TariffDurationSeconds.GetValueOrDefault(SelectedTariff),
            AmountTiyin,
            DateTime.UtcNow,
            ReasonCode);

        await _cashAudit.RecordAsync(entry);
        ConfirmRequested?.Invoke();
    }

    [RelayCommand]
    public void Cancel() => CancelRequested?.Invoke();
}
