using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClubPay.Agent.Admin.ViewModels;
using ClubPay.Agent.Admin.Controls;
using ClubPay.Agent.Core.Models;

namespace ClubPay.Agent.Admin.Views;

public partial class AdminWindow : Window
{
    private AdminViewModel Vm => (AdminViewModel)DataContext;

    public AdminWindow(AdminViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private void OnPcSelected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is PcCard pc)
            Vm.SelectPcCommand.Execute(pc);
    }

    private void OnZoneFilter(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string tag)
            Vm.SetZoneFilterCommand.Execute(tag);
    }
}
