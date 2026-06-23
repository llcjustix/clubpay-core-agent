using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ClubPay.Agent.Admin.ViewModels;
using ClubPay.Agent.Admin.Views;

namespace ClubPay.Agent.Admin;

public partial class App : Application
{
    private readonly ServiceProvider _services;

    public App()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<AdminViewModel>();
        sc.AddSingleton<CashPaymentViewModel>();
        sc.AddSingleton<AdminWindow>();
        _services = sc.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services.GetRequiredService<AdminWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services.Dispose();
        base.OnExit(e);
    }
}

