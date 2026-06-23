using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.ViewModels;

public partial class AdminViewModel : ObservableObject
{
    private readonly DispatcherTimer _clock;

    [ObservableProperty] private string _currentTime = DateTime.Now.ToString("HH:mm");
    [ObservableProperty] private string _adminName = "Sardor";
    [ObservableProperty] private string _controllerStatus = "Online";
    [ObservableProperty] private bool _isControllerOnline = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPcDetailVisible))]
    private PcCard? _selectedPc;

    [ObservableProperty] private bool _isCashPaymentVisible;

    public bool IsPcDetailVisible => SelectedPc is not null;

    public ObservableCollection<PcCard> Pcs { get; } = [];

    // Zone filter tabs
    [ObservableProperty] private string _activeZoneFilter = "Hammasi";
    public string[] ZoneFilters { get; } = ["Hammasi", "Standart", "Pro", "VIP"];

    // Status summary
    public int ActiveCount => Pcs.Count(p => p.Status == PcStatus.Active);
    public int FrozenCount => Pcs.Count(p => p.Status == PcStatus.Frozen);
    public int FreeCount => Pcs.Count(p => p.Status == PcStatus.Free);
    public int SleepCount => Pcs.Count(p => p.Status == PcStatus.Sleeping);
    public int AttentionCount => Pcs.Count(p => p.Status == PcStatus.Attention);

    public AdminViewModel()
    {
        LoadDemoPcs();

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => CurrentTime = DateTime.Now.ToString("HH:mm");
        _clock.Start();
    }

    private void LoadDemoPcs()
    {
        var data = new (string id, ZoneType zone, PcStatus status, string time)[]
        {
            ("PC-01", ZoneType.Standard, PcStatus.Active,    "01:12 qoldi"),
            ("PC-02", ZoneType.Standard, PcStatus.Active,    "00:34 qoldi"),
            ("PC-03", ZoneType.Standard, PcStatus.Free,      "Bo'sh"),
            ("PC-04", ZoneType.Standard, PcStatus.Active,    "02:05 qoldi"),
            ("PC-05", ZoneType.Standard, PcStatus.Sleeping,  "Uxlayapti"),
            ("PC-06", ZoneType.Standard, PcStatus.Active,    "00:08 qoldi"),
            ("PC-07", ZoneType.Standard, PcStatus.Active,    "01:48 qoldi"),
            ("PC-08", ZoneType.Standard, PcStatus.Free,      "Bo'sh"),
            ("PC-09", ZoneType.Pro,      PcStatus.Active,    "00:45 qoldi"),
            ("PC-10", ZoneType.Pro,      PcStatus.Active,    "03:20 qoldi"),
            ("PC-11", ZoneType.Pro,      PcStatus.Attention, "Diqqat"),
            ("PC-12", ZoneType.Pro,      PcStatus.Active,    "00:45 qoldi"),
            ("PC-13", ZoneType.Pro,      PcStatus.Active,    "01:02 qoldi"),
            ("PC-14", ZoneType.Pro,      PcStatus.Frozen,    "To'lov kutilmoqda"),
            ("PC-15", ZoneType.Pro,      PcStatus.Active,    "00:25 qoldi"),
            ("PC-16", ZoneType.Vip,      PcStatus.Active,    "04:10 qoldi"),
            ("PC-17", ZoneType.Vip,      PcStatus.Active,    "00:55 qoldi"),
            ("PC-18", ZoneType.Vip,      PcStatus.Free,      "Bo'sh"),
            ("PC-19", ZoneType.Vip,      PcStatus.Sleeping,  "Uxlayapti"),
            ("PC-20", ZoneType.Vip,      PcStatus.Active,    "02:38 qoldi"),
        };

        foreach (var (id, zone, status, time) in data)
            Pcs.Add(new PcCard
            {
                PcId = id,
                Zone = zone,
                Status = status,
                StatusText = time,
                TimeRemaining = time
            });

        NotifyStatusSummary();
    }

    [RelayCommand]
    public void SelectPc(PcCard pc) => SelectedPc = SelectedPc == pc ? null : pc;

    [RelayCommand]
    public void CloseDetail() => SelectedPc = null;

    [RelayCommand]
    public void OpenCashPayment() => IsCashPaymentVisible = true;

    [RelayCommand]
    public void CloseCashPayment() => IsCashPaymentVisible = false;

    [RelayCommand]
    public void SetZoneFilter(string filter)
    {
        ActiveZoneFilter = filter;
        // TODO: filter Pcs collection
    }

    [RelayCommand]
    public async Task WakePcAsync(PcCard pc)
    {
        await Task.Delay(100); // placeholder for WoL command
    }

    [RelayCommand]
    public async Task SleepPcAsync(PcCard pc)
    {
        await Task.Delay(100); // placeholder for sleep command
    }

    [RelayCommand]
    public async Task EndSessionAsync(PcCard pc)
    {
        await Task.Delay(100); // placeholder
    }

    private void NotifyStatusSummary()
    {
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(FrozenCount));
        OnPropertyChanged(nameof(FreeCount));
        OnPropertyChanged(nameof(SleepCount));
        OnPropertyChanged(nameof(AttentionCount));
    }
}
