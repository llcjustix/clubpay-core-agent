using System.Windows.Controls;
using System.Windows.Input;
using ClubPay.Agent.Client.ViewModels;

namespace ClubPay.Agent.Client.Views;

public partial class LockScreenView : UserControl
{
    private LockScreenViewModel Vm => (LockScreenViewModel)DataContext;

    public LockScreenView() => InitializeComponent();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (Vm.IsCodeInputVisible) Vm.ToggleCodeInputCommand.Execute(null);
                break;

            case Key.Back:
                Vm.AppendKeyCommand.Execute("\b");
                break;

            case Key.Enter:
                if (Vm.IsCodeInputVisible)
                    Vm.SubmitCodeCommand.Execute(null);
                else
                    Vm.ToggleCodeInputCommand.Execute(null);
                break;

            default:
                var ch = KeyToChar(e.Key);
                if (ch != null)
                    Vm.AppendKeyCommand.Execute(ch);
                else if (!Vm.IsCodeInputVisible)
                    Vm.ToggleCodeInputCommand.Execute(null);
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
