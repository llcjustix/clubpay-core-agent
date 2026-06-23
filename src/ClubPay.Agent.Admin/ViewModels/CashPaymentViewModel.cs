using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.ViewModels;

public partial class CashPaymentViewModel : ObservableObject
{
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

    public string AmountLabel => $"{AmountTiyin / 100m:N0} so'm";

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
    public void Confirm()
    {
        if (PinInput.Length < 4) return;
        // TODO: verify PIN via admin service
        ConfirmRequested?.Invoke();
    }

    [RelayCommand]
    public void Cancel() => CancelRequested?.Invoke();
}
