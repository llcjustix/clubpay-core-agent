using System.Windows;
using ClubPay.Agent.Admin.Views;
using Microsoft.Extensions.Configuration;

namespace ClubPay.Agent.Admin;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The Manager client intentionally renders the production back office
        // rather than carrying a second, incomplete copy of its operations.
        // That means an EXE always has the same PC, payment, staff and settings
        // workflows as the web admin and cannot silently drift from it.
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .Build();

        var window = new AdminWindow(config);
        MainWindow = window;
        window.Show();
    }
}
