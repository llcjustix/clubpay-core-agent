using System.Windows;
using System.Windows.Controls;
using ClubPay.Agent.Admin.ViewModels;

namespace ClubPay.Agent.Admin.Controls;

public partial class PcDetailPanel : UserControl
{
    private AdminViewModel Vm => (AdminViewModel)DataContext;

    public PcDetailPanel() => InitializeComponent();

    private void OnClose(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => Vm.CloseDetailCommand.Execute(null);

    private void OnCashPaymentClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => Vm.OpenCashPaymentCommand.Execute(null);
}
