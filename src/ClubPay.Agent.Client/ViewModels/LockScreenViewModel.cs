using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClubPay.Agent.Core.Services;
using ClubPay.Agent.Client.Services;

namespace ClubPay.Agent.Client.ViewModels;

/// <summary>Shows the normal payment screen or the focused reservation check-in screen.</summary>
public partial class LockScreenViewModel : ObservableObject
{
    private readonly IAgentService _agent;
    private readonly QrCodeService _qr;
    private readonly DispatcherTimer _clock;
    private TimeZoneInfo _clubTimeZone = TimeZoneInfo.Local;

    [ObservableProperty] private string _pcId = "PC-12";
    [ObservableProperty] private string _zoneLabel = "Standard Zone · Standart Zona";
    [ObservableProperty] private string _clubName = "ClubPay";
    [ObservableProperty] private string _currentTime = "--:--";
    [ObservableProperty] private bool _isReserved;
    [ObservableProperty] private string _reservationStart = string.Empty;
    [ObservableProperty] private string _arrivalDeadline = string.Empty;
    [ObservableProperty] private bool _canEnterReservationCode;
    [ObservableProperty] private bool _checkingIn;
    [ObservableProperty] private string _reservationEntry = string.Empty;
    [ObservableProperty] private string _reservationWaitText = string.Empty;
    [ObservableProperty] private string _reservationError = string.Empty;

    [ObservableProperty] private BitmapImage? _payQrImage;
    [ObservableProperty] private BitmapImage? _wifiQrImage;

    public bool CanSubmitReservationCode =>
        CanEnterReservationCode && !CheckingIn && ReservationEntry.Length == 6 && ReservationEntry.All(char.IsDigit);

    public LockScreenViewModel(IAgentService agent, QrCodeService qr)
    {
        _agent = agent;
        _qr = qr;
        UpdateIdentity();

        _clock = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => RefreshClock();
        _clock.Start();

        _agent.StaticPaymentQrUrlChanged += RefreshPaymentQr;
        _agent.BootstrapChanged += RefreshIdentity;
        GenerateQrCodes();
    }

    partial void OnReservationEntryChanged(string value)
    {
        if (!string.IsNullOrEmpty(ReservationError))
            ReservationError = string.Empty;
        OnPropertyChanged(nameof(CanSubmitReservationCode));
    }

    partial void OnCheckingInChanged(bool value) => OnPropertyChanged(nameof(CanSubmitReservationCode));
    partial void OnCanEnterReservationCodeChanged(bool value) => OnPropertyChanged(nameof(CanSubmitReservationCode));

    [RelayCommand]
    private async Task CheckInReservationAsync()
    {
        if (!CanSubmitReservationCode)
            return;
        CheckingIn = true;
        ReservationError = string.Empty;
        try
        {
            await _agent.CheckInReservationAsync(ReservationEntry);
            ReservationWaitText = "Игра запускается…";
        }
        catch (Exception ex)
        {
            ReservationError = ex.Message;
        }
        finally
        {
            CheckingIn = false;
        }
    }

    private void GenerateQrCodes()
    {
        PayQrImage = string.IsNullOrWhiteSpace(_agent.StaticPaymentQrUrl)
            ? null
            : _qr.Generate(_agent.StaticPaymentQrUrl, 300);
        WifiQrImage = _qr.GenerateWifi(_agent.WifiSsid, _agent.WifiPassword, pixelSize: 108);
    }

    private void RefreshPaymentQr()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            GenerateQrCodes();
        else
            _ = dispatcher.InvokeAsync(GenerateQrCodes);
    }

    private void RefreshIdentity()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            UpdateIdentity();
        else
            _ = dispatcher.InvokeAsync(UpdateIdentity);
    }

    private void UpdateIdentity()
    {
        PcId = _agent.PcId;
        ClubName = _agent.ClubName;
        ZoneLabel = _agent.ZoneName;
        _clubTimeZone = ResolveTimeZone(_agent.TimeZoneId);
        IsReserved = _agent.HasActiveReservation;
        ReservationStart = _agent.ReservationStartsAt is { } start
            ? TimeZoneInfo.ConvertTime(start, _clubTimeZone).ToString("HH:mm")
            : string.Empty;
        ArrivalDeadline = _agent.ReservationCheckinDeadline is { } deadline
            ? TimeZoneInfo.ConvertTime(deadline, _clubTimeZone).ToString("HH:mm")
            : string.Empty;
        if (!IsReserved)
        {
            ReservationEntry = string.Empty;
            ReservationError = string.Empty;
        }
        RefreshClock();
    }

    private void RefreshClock()
    {
        CurrentTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _clubTimeZone).ToString("HH:mm");
        var now = DateTimeOffset.UtcNow;
        var startsAt = _agent.ReservationStartsAt;
        var deadline = _agent.ReservationCheckinDeadline;
        CanEnterReservationCode = IsReserved && startsAt is not null && deadline is not null && now >= startsAt && now <= deadline;
        ReservationWaitText = !IsReserved
            ? string.Empty
            : CanEnterReservationCode
                ? CheckingIn ? "Проверяем код…" : "Введите шестизначный код из приложения ClubPay."
                : startsAt is null
                    ? string.Empty
                    : $"Код можно ввести с {TimeZoneInfo.ConvertTime(startsAt.Value, _clubTimeZone):HH:mm}.";
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        foreach (var candidate in new[] { timeZoneId, "Asia/Tashkent", "West Asia Standard Time" })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    public void Reset() => GenerateQrCodes();
}
