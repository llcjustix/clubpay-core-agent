using CommunityToolkit.Mvvm.ComponentModel;

namespace ClubPay.Agent.Core.Models;

/// <summary>Mutable so a live ControllerHub can push status updates from Agent events —
/// property names are unchanged from the original init-only shape, so existing XAML bindings
/// (PcCardControl, PcDetailPanel) keep working unmodified.</summary>
public partial class PcCard : ObservableObject
{
    [ObservableProperty] private string _pcId = string.Empty;
    [ObservableProperty] private string _externalPcId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoneLabel))]
    private ZoneType _zone;

    [ObservableProperty] private PcStatus _status;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _timeRemaining = string.Empty;

    public string ZoneLabel => Zone switch { ZoneType.Pro => "Pro", ZoneType.Vip => "VIP", _ => "Standart" };
}
