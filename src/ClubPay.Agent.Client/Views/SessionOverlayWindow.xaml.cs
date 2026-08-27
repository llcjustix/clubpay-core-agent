using System.Windows;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.Views;

public partial class SessionOverlayWindow : Window
{
    public static SessionOverlayWindow? Instance { get; private set; }
    private readonly IClientSessionEndService _sessionEnd;
    private readonly QrCodeService _qr;
    private readonly LocalizationService _localizer;

    public SessionOverlayWindow(
        ViewModels.MainViewModel vm,
        IClientSessionEndService sessionEnd,
        QrCodeService qr,
        LocalizationService localizer)
    {
        DataContext = vm;
        _sessionEnd = sessionEnd;
        _qr = qr;
        _localizer = localizer;
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
            var result = await _sessionEnd.EndCurrentSessionAsync();
            if (!result.IsProfileSession)
                new VoucherDeliveryDialog(result, _qr).ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(_localizer.Format("EndSessionFailed", ex.Message), "ClubPay",
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
