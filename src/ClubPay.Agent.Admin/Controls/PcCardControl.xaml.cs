using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClubPay.Agent.Core.Models;
using ClubPay.Agent.Admin.ViewModels;

namespace ClubPay.Agent.Admin.Controls;

public partial class PcCardControl : UserControl
{
    public static readonly RoutedEvent PcSelectedEvent =
        EventManager.RegisterRoutedEvent(nameof(PcSelected), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(PcCardControl));

    public event RoutedEventHandler PcSelected
    {
        add => AddHandler(PcSelectedEvent, value);
        remove => RemoveHandler(PcSelectedEvent, value);
    }

    public PcCardControl() => InitializeComponent();

    private void OnCardClick(object sender, MouseButtonEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(PcSelectedEvent, DataContext));
}
