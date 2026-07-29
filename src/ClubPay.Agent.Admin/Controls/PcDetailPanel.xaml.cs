using System.Windows;
using System.Windows.Controls;
using ClubPay.Agent.Admin.ViewModels;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Controls;

public partial class PcDetailPanel : UserControl
{
    private AdminViewModel Vm => (AdminViewModel)DataContext;

    public PcDetailPanel() => InitializeComponent();

    private void OnClose(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => Vm.CloseDetailCommand.Execute(null);

    private void OnCashPaymentClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => Vm.OpenCashPaymentCommand.Execute(null);

    private void OnEndSessionClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Vm.SelectedPc is { } pc)
            Vm.EndSessionCommand.Execute(pc);
    }

    private void OnWakeSleepToggleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Vm.SelectedPc is not { } pc)
            return;

        if (pc.Status == PcStatus.Sleeping)
            Vm.WakePcCommand.Execute(pc);
        else
            Vm.SleepPcCommand.Execute(pc);
    }
}
