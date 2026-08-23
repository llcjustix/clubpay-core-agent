using System.Windows;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.Views;

public partial class SessionOverlayWindow : Window
{
    public static SessionOverlayWindow? Instance { get; private set; }
    private readonly IClientSessionEndService _sessionEnd;
    private readonly QrCodeService _qr;

    public SessionOverlayWindow(
        ViewModels.MainViewModel vm,
        IClientSessionEndService sessionEnd,
        QrCodeService qr)
    {
        DataContext = vm;
        _sessionEnd = sessionEnd;
        _qr = qr;
        Instance = this;
        InitializeComponent();
        Loaded += (_, _) => PositionTopRight();
        ActiveSessionControl.EndSessionRequested += RequestSessionEndAsync;
    }

    private async Task RequestSessionEndAsync()
    {
        var dialog = new EndSessionDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var result = await _sessionEnd.EndCurrentSessionAsync(dialog.RecipientPhone, dialog.RecipientConsent);
            new VoucherDeliveryDialog(result, _qr).ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось завершить сеанс: " + ex.Message, "ClubPay",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PositionTopRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 20;
        Top  = area.Top  + 20;
    }
}
