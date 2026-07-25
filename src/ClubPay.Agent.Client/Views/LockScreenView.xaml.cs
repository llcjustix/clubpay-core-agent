using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ClubPay.Agent.Client.Views;

public partial class LockScreenView : UserControl
{
    public LockScreenView()
    {
        InitializeComponent();

        // View-only focus plumbing: focusing during the visibility change itself is unreliable in WPF,
        // so it is deferred one dispatcher hop.
        CodeBox.IsVisibleChanged += OnCodeBoxVisibleChanged;
    }

    private void OnCodeBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
            return;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            CodeBox.Focus();
            Keyboard.Focus(CodeBox);
        });
    }
}
