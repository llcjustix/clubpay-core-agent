using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core;
using ClubPay.Agent.Core.Contracts.Enums;
using ClubPay.Agent.Core.Contracts.Payloads;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Admin.Services.Controller;

namespace ClubPay.Agent.Admin.ViewModels;

public partial class AdminViewModel : ObservableObject
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(Constants.ControllerChannel.SendTimeoutSeconds);

    private readonly IPcStateStore _stateStore;
    private readonly ControllerHubService _hub;
    private readonly ILogger<AdminViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly ICollectionView _pcsView;

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

    public CashPaymentViewModel CashPayment { get; }

    public AdminViewModel(
        IPcRegistry registry,
        IPcStateStore stateStore,
        ControllerHubService hub,
        CashPaymentViewModel cashPayment,
        ILogger<AdminViewModel> logger)
    {
        _stateStore = stateStore;
        _hub = hub;
        _logger = logger;

        CashPayment = cashPayment;
        CashPayment.ConfirmRequested += () => IsCashPaymentVisible = false;
        CashPayment.CancelRequested += () => IsCashPaymentVisible = false;
        // Falls back to the calling thread's own dispatcher when no WPF Application is running
        // (unit tests) — in the real app this is always Application.Current.Dispatcher.
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        foreach (var entry in registry.All)
        {
            var card = new PcCard { PcId = entry.PcId, ExternalPcId = entry.ExternalPcId, Zone = entry.Zone };
            var state = _stateStore.Get(entry.ExternalPcId);
            if (state is not null)
                PcCardMapper.Apply(card, state);
            Pcs.Add(card);
        }

        _pcsView = CollectionViewSource.GetDefaultView(Pcs);
        _pcsView.Filter = FilterByZone;

        _stateStore.Changed += OnStateChanged;
        NotifyStatusSummary();

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => CurrentTime = DateTime.Now.ToString("HH:mm");
        _clock.Start();
    }

    private bool FilterByZone(object obj) =>
        ActiveZoneFilter == "Hammasi" || obj is not PcCard card || card.ZoneLabel == ActiveZoneFilter;

    private void OnStateChanged(string externalPcId)
    {
        _dispatcher.Invoke(() =>
        {
            var card = Pcs.FirstOrDefault(p => p.ExternalPcId == externalPcId);
            var state = _stateStore.Get(externalPcId);
            if (card is null || state is null)
                return;

            PcCardMapper.Apply(card, state);
            NotifyStatusSummary();
        });
    }

    [RelayCommand]
    public void SelectPc(PcCard pc) => SelectedPc = SelectedPc == pc ? null : pc;

    [RelayCommand]
    public void CloseDetail() => SelectedPc = null;

    [RelayCommand]
    public void OpenCashPayment()
    {
        if (SelectedPc is not { } pc)
            return;

        CashPayment.ManagerId = AdminName;
        CashPayment.Load(pc);
        IsCashPaymentVisible = true;
    }

    [RelayCommand]
    public void CloseCashPayment() => IsCashPaymentVisible = false;

    [RelayCommand]
    public void SetZoneFilter(string filter)
    {
        ActiveZoneFilter = filter;
        _pcsView.Refresh();
    }

    [RelayCommand]
    public Task WakePcAsync(PcCard pc) =>
        SendAsync(pc, Constants.ControllerChannel.CommandName.Wake, new WakePayload(pc.ExternalPcId));

    [RelayCommand]
    public Task SleepPcAsync(PcCard pc) =>
        SendAsync(pc, Constants.ControllerChannel.CommandName.Sleep, new SleepPayload(pc.ExternalPcId));

    [RelayCommand]
    public Task LockPcAsync(PcCard pc) =>
        SendAsync(pc, Constants.ControllerChannel.CommandName.Lock, new LockPayload(pc.ExternalPcId, "manager"));

    [RelayCommand]
    public Task UnlockPcAsync(PcCard pc) =>
        SendAsync(pc, Constants.ControllerChannel.CommandName.Unlock, new UnlockPayload(pc.ExternalPcId, "manager"));

    [RelayCommand]
    public Task SetRepairAsync(PcCard pc) =>
        SendAsync(pc, Constants.ControllerChannel.CommandName.SetRepair, new SetRepairPayload(pc.ExternalPcId, On: true));

    [RelayCommand]
    public async Task EndSessionAsync(PcCard pc)
    {
        var coreSessionId = _stateStore.Get(pc.ExternalPcId)?.CoreSessionId;
        if (string.IsNullOrEmpty(coreSessionId))
        {
            _logger.LogWarning("EndSession so'ralgan, lekin faol sessiya yo'q: {ExternalPcId}", pc.ExternalPcId);
            return;
        }

        await SendAsync(pc, Constants.ControllerChannel.CommandName.EndSession,
            new EndSessionPayload(coreSessionId, EndReason.Manager));
    }

    private async Task SendAsync(PcCard pc, string commandName, object payload)
    {
        var result = await _hub.SendCommandAsync(pc.ExternalPcId, commandName, payload, CommandTimeout);
        if (result.Status != "ok")
        {
            _logger.LogWarning(
                "Komanda muvaffaqiyatsiz: {Command} {ExternalPcId} -> {ErrorCode} {Message}",
                commandName, pc.ExternalPcId, result.ErrorCode, result.Message);
        }
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
