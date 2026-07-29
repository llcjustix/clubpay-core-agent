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

    public static readonly RoutedEvent PcWakeRequestedEvent =
        EventManager.RegisterRoutedEvent(nameof(PcWakeRequested), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(PcCardControl));

    public static readonly RoutedEvent PcSleepRequestedEvent =
        EventManager.RegisterRoutedEvent(nameof(PcSleepRequested), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(PcCardControl));

    public static readonly RoutedEvent PcEndRequestedEvent =
        EventManager.RegisterRoutedEvent(nameof(PcEndRequested), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(PcCardControl));

    public event RoutedEventHandler PcSelected
    {
        add => AddHandler(PcSelectedEvent, value);
        remove => RemoveHandler(PcSelectedEvent, value);
    }

    public event RoutedEventHandler PcWakeRequested
    {
        add => AddHandler(PcWakeRequestedEvent, value);
        remove => RemoveHandler(PcWakeRequestedEvent, value);
    }

    public event RoutedEventHandler PcSleepRequested
    {
        add => AddHandler(PcSleepRequestedEvent, value);
        remove => RemoveHandler(PcSleepRequestedEvent, value);
    }

    public event RoutedEventHandler PcEndRequested
    {
        add => AddHandler(PcEndRequestedEvent, value);
        remove => RemoveHandler(PcEndRequestedEvent, value);
    }

    public PcCardControl() => InitializeComponent();

    private void OnCardClick(object sender, MouseButtonEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(PcSelectedEvent, DataContext));

    private void OnWakeClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(PcWakeRequestedEvent, DataContext));
    }

    private void OnSleepClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(PcSleepRequestedEvent, DataContext));
    }

    private void OnEndClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(PcEndRequestedEvent, DataContext));
    }
}
