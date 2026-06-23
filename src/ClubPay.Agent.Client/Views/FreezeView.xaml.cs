using System.Windows.Controls;
using System.Windows.Input;
using ClubPay.Agent.Client.ViewModels;

namespace ClubPay.Agent.Client.Views;

public partial class FreezeView : UserControl
{
    private FreezeViewModel Vm => (FreezeViewModel)DataContext;

    public FreezeView() => InitializeComponent();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Back:
                Vm.AppendVoucherCommand.Execute("\b");
                break;
            case Key.Enter:
                Vm.SubmitVoucherCommand.Execute(null);
                break;
            default:
                var ch = KeyToChar(e.Key);
                if (ch != null) Vm.AppendVoucherCommand.Execute(ch);
                break;
        }
        e.Handled = true;
    }

    private static string? KeyToChar(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(),
        >= Key.A and <= Key.Z => key.ToString(),
        Key.OemMinus or Key.Subtract => "-",
        _ => null
    };
}
