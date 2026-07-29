using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ClubPay.Agent.Core.Services;
using ClubPay.Agent.Admin.Services;
using ClubPay.Agent.Admin.Services.Controller;
using ClubPay.Agent.Admin.ViewModels;
using ClubPay.Agent.Admin.Views;

namespace ClubPay.Agent.Admin;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(config);
        sc.AddLogging(b => b.AddDebug());
        sc.AddSingleton<IPcRegistry, PcRegistry>();
        sc.AddSingleton<IPcStateStore, PcStateStore>();
        sc.AddSingleton<ControllerHubService>();
        sc.AddSingleton<ICashAuditService, CashAuditService>();
        sc.AddSingleton<IManagerPinService, ManagerPinService>();
        sc.AddSingleton<AdminViewModel>();
        sc.AddSingleton<CashPaymentViewModel>();
        sc.AddSingleton<AdminWindow>();

        _services = sc.BuildServiceProvider();

        await _services.GetRequiredService<ControllerHubService>().StartAsync();
        _services.GetRequiredService<AdminWindow>().Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            await _services.GetRequiredService<ControllerHubService>().StopAsync();
            _services.Dispose();
        }
        base.OnExit(e);
    }
}
