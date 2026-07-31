using System.ComponentModel;
using System.Windows.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ClubPay.Agent.Admin.Services.Controller;
using ClubPay.Agent.Admin.ViewModels;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Core.Services;

namespace ClubPay.Agent.Admin.Tests;

public class AdminViewModelTests
{
    private static (AdminViewModel Vm, IPcStateStore Store) Build(params (string ExternalPcId, string PcId, string Zone)[] pcs)
    {
        // Hub is never started in these tests (AdminViewModel only needs it constructed to send
        // commands, which return AgentOffline against an unstarted hub) — port is unused.
        var data = new Dictionary<string, string?> { ["Controller:ListenPrefix"] = "http://localhost:18888/" };
        for (int i = 0; i < pcs.Length; i++)
        {
            data[$"Controller:Pcs:{i}:ExternalPcId"] = pcs[i].ExternalPcId;
            data[$"Controller:Pcs:{i}:PcId"] = pcs[i].PcId;
            data[$"Controller:Pcs:{i}:Zone"] = pcs[i].Zone;
            data[$"Controller:Pcs:{i}:AgentToken"] = "token";
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        var registry = new PcRegistry(config);
        var store = new PcStateStore(registry, NullLogger<PcStateStore>.Instance);
        var hub = new ControllerHubService(
            config, registry, store, new EventIdempotencyStore(), NullLogger<ControllerHubService>.Instance);
        var cashPayment = new CashPaymentViewModel(
            Mock.Of<ICashAuditService>(), Mock.Of<IManagerPinService>(), NullLogger<CashPaymentViewModel>.Instance);
        var vm = new AdminViewModel(registry, store, hub, cashPayment, NullLogger<AdminViewModel>.Instance);
        return (vm, store);
    }

    [Fact]
    public void Constructor_PopulatesPcsFromRegistry_AllOfflineInitially()
    {
        var (vm, _) = Build(("pc-1", "PC-01", "Standard"), ("pc-2", "PC-02", "Pro"));

        Assert.Equal(2, vm.Pcs.Count);
        Assert.All(vm.Pcs, p => Assert.Equal(PcStatus.Offline, p.Status));
    }

    [Fact]
    public void SetZoneFilter_Pro_ShowsOnlyProCardsInDefaultView()
    {
        var (vm, _) = Build(("pc-1", "PC-01", "Standard"), ("pc-2", "PC-02", "Pro"));

        vm.SetZoneFilterCommand.Execute("Pro");

        var view = CollectionViewSource.GetDefaultView(vm.Pcs);
        var visible = view.Cast<PcCard>().ToList();
        Assert.Single(visible);
        Assert.Equal("PC-02", visible[0].PcId);
    }

    [Fact]
    public void SetZoneFilter_Hammasi_ShowsAllCards()
    {
        var (vm, _) = Build(("pc-1", "PC-01", "Standard"), ("pc-2", "PC-02", "Pro"));
        vm.SetZoneFilterCommand.Execute("Pro");

        vm.SetZoneFilterCommand.Execute("Hammasi");

        var view = CollectionViewSource.GetDefaultView(vm.Pcs);
        Assert.Equal(2, view.Cast<PcCard>().Count());
    }

    [Fact]
    public void StateStoreChanged_UpdatesMatchingCardAndSummaryCounts()
    {
        var (vm, store) = Build(("pc-1", "PC-01", "Standard"));

        store.MarkConnected("pc-1");

        var card = vm.Pcs.Single();
        Assert.Equal(PcStatus.Free, card.Status);
        Assert.Equal(1, vm.FreeCount);
        Assert.Equal(0, vm.ActiveCount);
    }

    [Fact]
    public void ActiveCount_UpdatesWhenPropertyChangedFired()
    {
        var (vm, store) = Build(("pc-1", "PC-01", "Standard"));
        var raisedProperties = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        store.MarkConnected("pc-1");

        Assert.Contains(nameof(AdminViewModel.FreeCount), raisedProperties);
    }
}
