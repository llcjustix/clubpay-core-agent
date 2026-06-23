using System.Windows.Controls;
using System.Windows.Input;
using ClubPay.Agent.Admin.ViewModels;

namespace ClubPay.Agent.Admin.Views;

public partial class CashPaymentDialog : UserControl
{
    private AdminViewModel Vm => (AdminViewModel)DataContext;

    public CashPaymentDialog() => InitializeComponent();

    private void OnConfirm(object sender, MouseButtonEventArgs e)
        => Vm.CloseCashPaymentCommand.Execute(null); // TODO: real confirm flow

    private void OnCancel(object sender, MouseButtonEventArgs e)
        => Vm.CloseCashPaymentCommand.Execute(null);
}
