using System.Windows.Controls;
using System.Windows.Input;
using ClubPay.Agent.Admin.ViewModels;

namespace ClubPay.Agent.Admin.Views;

public partial class CashPaymentDialog : UserControl
{
    private CashPaymentViewModel Vm => (CashPaymentViewModel)DataContext;

    public CashPaymentDialog() => InitializeComponent();

    private void OnConfirm(object sender, MouseButtonEventArgs e)
        => Vm.ConfirmCommand.Execute(null);

    private void OnCancel(object sender, MouseButtonEventArgs e)
        => Vm.CancelCommand.Execute(null);
}
